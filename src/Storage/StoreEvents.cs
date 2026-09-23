using System.Text.Json.Nodes;
using IsotopeMix.Model;
using Microsoft.Data.Sqlite;

namespace IsotopeMix.Storage;

public sealed class BranchConflictException : Exception
{
    public string ExpectedHead { get; }
    public string ActualHead { get; }
    public BranchConflictException(string expected, string actual)
        : base($"branch head conflict: expected {expected}, actual {actual}")
    {
        ExpectedHead = expected; ActualHead = actual;
    }
}

public sealed partial class Store
{
    public void CreateBranch(SqliteConnection conn, SqliteTransaction tx, Branch branch)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO branch(branch_id,label,parent_event_id,created_at,head_event_id)
            VALUES($id,$l,$p,$t,$h)";
        cmd.Parameters.AddWithValue("$id", branch.BranchId);
        cmd.Parameters.AddWithValue("$l", branch.Label);
        cmd.Parameters.AddWithValue("$p", branch.ParentEventId);
        cmd.Parameters.AddWithValue("$t", branch.CreatedAt);
        cmd.Parameters.AddWithValue("$h", branch.HeadEventId);
        cmd.ExecuteNonQuery();
    }

    public Branch? GetBranch(string branchId)
    {
        using var conn = _db.Open();
        return GetBranch(conn, null, branchId);
    }

    public Branch? GetBranch(SqliteConnection conn, SqliteTransaction? tx, string branchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT branch_id,label,parent_event_id,created_at,head_event_id FROM branch WHERE branch_id=$id";
        cmd.Parameters.AddWithValue("$id", branchId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Branch
        {
            BranchId = r.GetString(0), Label = r.GetString(1), ParentEventId = r.GetString(2),
            CreatedAt = r.GetString(3), HeadEventId = r.GetString(4)
        };
    }

    public List<Branch> ListBranches()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id,label,parent_event_id,created_at,head_event_id FROM branch ORDER BY created_at, branch_id";
        var list = new List<Branch>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Branch { BranchId = r.GetString(0), Label = r.GetString(1),
                ParentEventId = r.GetString(2), CreatedAt = r.GetString(3), HeadEventId = r.GetString(4) });
        return list;
    }

    // Append an event and advance the branch head. Optimistic concurrency:
    // expectedHead must match the current head, otherwise the second writer
    // sees a conflict (409 at the API layer) instead of a silent overwrite.
    public EventRecord AppendEvent(SqliteConnection conn, SqliteTransaction tx,
        string branchId, string type, string actor, JsonObject payload,
        string? expectedHead = null, string? createdAtOverride = null)
    {
        var branch = GetBranch(conn, tx, branchId) ?? throw new InvalidOperationException($"unknown branch {branchId}");
        if (expectedHead is not null && branch.HeadEventId != expectedHead)
            throw new BranchConflictException(expectedHead, branch.HeadEventId);

        string parent = branch.HeadEventId;
        string createdAt = createdAtOverride ?? Clock.IsoNow();
        string eventId = "evt-" + DeterministicEventId(branchId, parent, type, createdAt, payload);
        var rec = new EventRecord
        {
            EventId = eventId, BranchId = branchId, ParentEventId = parent,
            Type = type, Actor = actor, CreatedAt = createdAt, Payload = payload
        };
        InsertEvent(conn, tx, rec);

        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE branch SET head_event_id=$h WHERE branch_id=$b";
        upd.Parameters.AddWithValue("$h", eventId);
        upd.Parameters.AddWithValue("$b", branchId);
        upd.ExecuteNonQuery();

        return rec;
    }

    // Insert an event without moving a head (used during import replay).
    public EventRecord InsertReplayedEvent(SqliteConnection conn, SqliteTransaction tx, EventRecord rec)
    {
        InsertEvent(conn, tx, rec);
        using var upd = conn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE branch SET head_event_id=$h WHERE branch_id=$b";
        upd.Parameters.AddWithValue("$h", rec.EventId);
        upd.Parameters.AddWithValue("$b", rec.BranchId);
        upd.ExecuteNonQuery();
        return rec;
    }

    private static void InsertEvent(SqliteConnection conn, SqliteTransaction tx, EventRecord rec)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO event_record
            (event_id,branch_id,parent_event_id,type,actor,created_at,payload_json)
            VALUES($id,$b,$p,$t,$a,$c,$payload)";
        cmd.Parameters.AddWithValue("$id", rec.EventId);
        cmd.Parameters.AddWithValue("$b", rec.BranchId);
        cmd.Parameters.AddWithValue("$p", rec.ParentEventId);
        cmd.Parameters.AddWithValue("$t", rec.Type);
        cmd.Parameters.AddWithValue("$a", rec.Actor);
        cmd.Parameters.AddWithValue("$c", rec.CreatedAt);
        cmd.Parameters.AddWithValue("$payload", JsonIo.Canonical(rec.Payload));
        cmd.ExecuteNonQuery();
    }

    public static string DeterministicEventId(string branchId, string parent, string type,
        string createdAt, JsonObject payload)
    {
        var envelope = new JsonObject
        {
            ["branch"] = branchId,
            ["parent"] = parent,
            ["type"] = type,
            ["at"] = createdAt,
            ["payload"] = payload
        };
        return JsonIo.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(JsonIo.Canonical(envelope)))[..24];
    }

    public List<EventRecord> BranchEvents(string branchId)
    {
        using var conn = _db.Open();
        return BranchEvents(conn, null, branchId);
    }

    public List<EventRecord> BranchEvents(SqliteConnection conn, SqliteTransaction? tx, string branchId)
    {
        var list = new List<EventRecord>();
        var byId = new Dictionary<string, EventRecord>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT seq,event_id,branch_id,parent_event_id,type,actor,created_at,payload_json
                FROM event_record WHERE branch_id=$b ORDER BY seq";
            cmd.Parameters.AddWithValue("$b", branchId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var e = new EventRecord
                {
                    Seq = r.GetInt64(0), EventId = r.GetString(1), BranchId = r.GetString(2),
                    ParentEventId = r.GetString(3), Type = r.GetString(4), Actor = r.GetString(5),
                    CreatedAt = r.GetString(6), Payload = JsonIo.ParseObject(r.GetString(7))
                };
                list.Add(e);
                byId[e.EventId] = e;
            }
        }
        // Return events reachable from head (forks that branched off are excluded).
        var head = GetBranch(conn, tx, branchId)?.HeadEventId ?? "";
        if (head == "" || !byId.ContainsKey(head)) return list;
        var reachable = new List<EventRecord>();
        var current = head;
        while (current != "" && byId.TryGetValue(current, out var e))
        {
            reachable.Add(e);
            current = e.ParentEventId;
        }
        reachable.Reverse();
        return reachable;
    }

    public EventRecord? GetEvent(string eventId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT seq,event_id,branch_id,parent_event_id,type,actor,created_at,payload_json
            FROM event_record WHERE event_id=$id";
        cmd.Parameters.AddWithValue("$id", eventId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new EventRecord
        {
            Seq = r.GetInt64(0), EventId = r.GetString(1), BranchId = r.GetString(2),
            ParentEventId = r.GetString(3), Type = r.GetString(4), Actor = r.GetString(5),
            CreatedAt = r.GetString(6), Payload = JsonIo.ParseObject(r.GetString(7))
        };
    }

    public long EventSeq(string eventId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT seq FROM event_record WHERE event_id=$id";
        cmd.Parameters.AddWithValue("$id", eventId);
        var v = cmd.ExecuteScalar();
        return v is null ? throw new KeyNotFoundException(eventId) : Convert.ToInt64(v);
    }
}

public static class Clock
{
    public static Func<DateTimeOffset> NowFunc { get; set; } = () => DateTimeOffset.UtcNow;
    public static string IsoNow() => NowFunc().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
}

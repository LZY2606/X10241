using System.Text.Json;
using IsotopeApp.Domain;
using Microsoft.Data.Sqlite;

namespace IsotopeApp.Storage;

/// <summary>
/// Persistence boundary. Writes are transactional (a failed import leaves no
/// half records), every interpretation row is versioned, and events are
/// append-only with optimistic concurrency on the branch head.
/// </summary>
public sealed partial class Store
{
    private readonly Database _db;
    public Store(Database db) { _db = db; }
    private SqliteConnection C => _db.Connection;

    // ---------- branches ----------
    public void EnsureMainBranch()
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO branches(branch_id,name,parent_branch_id,created_from_event_id,created_at) " +
                          "VALUES ('main','main',NULL,'seed', $t)";
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Branch> ListBranches(bool includeArchived = false)
    {
        var list = new List<Branch>();
        using var cmd = C.CreateCommand();
        cmd.CommandText = includeArchived
            ? "SELECT branch_id,name,parent_branch_id,created_from_event_id,created_at,archived FROM branches ORDER BY created_at"
            : "SELECT branch_id,name,parent_branch_id,created_from_event_id,created_at,archived FROM branches WHERE archived=0 ORDER BY created_at";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Branch
            {
                BranchId = r.GetString(0), Name = r.GetString(1),
                ParentBranchId = r.IsDBNull(2) ? null : r.GetString(2),
                CreatedFromEventId = r.GetString(3), CreatedAt = r.GetString(4),
                Archived = r.GetInt32(5) == 1
            });
        return list;
    }

    public Branch GetBranch(string id)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT name,parent_branch_id,created_from_event_id,created_at,archived FROM branches WHERE branch_id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"branch {id}");
        return new Branch
        {
            BranchId = id, Name = r.GetString(0),
            ParentBranchId = r.IsDBNull(1) ? null : r.GetString(1),
            CreatedFromEventId = r.GetString(2), CreatedAt = r.GetString(3),
            Archived = r.GetInt32(4) == 1
        };
    }

    public string CreateBranch(string name, string parentBranchId, string fromEventId)
    {
        var id = Hashing.ContentId("branch", Hashing.Canonical(new { name, parentBranchId, fromEventId, t = IsoNow() }));
        using var tx = C.BeginTransaction();
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO branches(branch_id,name,parent_branch_id,created_from_event_id,created_at) " +
                          "VALUES($id,$name,$parent,$from,$t)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$parent", (object?)parentBranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$from", fromEventId);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
        tx.Commit();
        return id;
    }

    // ---------- events ----------
    public string BranchHead(string branchId)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT head_hash FROM events WHERE branch_id=$b ORDER BY seq DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branchId);
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? "seed" : (string)result;
    }

    public int NextSeq(string branchId)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(seq),0)+1 FROM events WHERE branch_id=$b";
        cmd.Parameters.AddWithValue("$b", branchId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public IReadOnlyList<ProjectionEvent> ListEvents(string branchId, int? upToSeq = null)
    {
        var list = new List<ProjectionEvent>();
        using var cmd = C.CreateCommand();
        cmd.CommandText = upToSeq is null
            ? "SELECT event_id,seq,branch_id,parent_event_id,type,occurred_at,actor,payload_json,dependencies_json,invalidated_json,head_hash FROM events WHERE branch_id=$b ORDER BY seq"
            : "SELECT event_id,seq,branch_id,parent_event_id,type,occurred_at,actor,payload_json,dependencies_json,invalidated_json,head_hash FROM events WHERE branch_id=$b AND seq<=$s ORDER BY seq";
        cmd.Parameters.AddWithValue("$b", branchId);
        if (upToSeq is int s) cmd.Parameters.AddWithValue("$s", s);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadEvent(r));
        return list;
    }

    public ProjectionEvent GetEvent(string eventId)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT event_id,seq,branch_id,parent_event_id,type,occurred_at,actor,payload_json,dependencies_json,invalidated_json,head_hash FROM events WHERE event_id=$e";
        cmd.Parameters.AddWithValue("$e", eventId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException(eventId);
        return ReadEvent(r);
    }

    private static ProjectionEvent ReadEvent(SqliteDataReader r) => new()
    {
        EventId = r.GetString(0),
        Seq = r.GetInt32(1),
        BranchId = r.GetString(2),
        ParentEventId = r.GetString(3),
        Type = r.GetString(4),
        OccurredAt = r.GetString(5),
        Actor = r.IsDBNull(6) ? null : r.GetString(6),
        Payload = JsonDocument.Parse(r.GetString(7)),
        Dependencies = JsonSerializer.Deserialize<List<DependencyRef>>(r.GetString(8), JsonOpts.Default) ?? new(),
        InvalidatedObjectIds = JsonSerializer.Deserialize<List<string>>(r.GetString(9), JsonOpts.Default) ?? new(),
        HeadHash = r.GetString(10)
    };

    /// <summary>
    /// Append an event. When <paramref name="expectedHead"/> is supplied it must
    /// match the current branch head or a <see cref="StaleHeadException"/> is
    /// raised with both event ids for the 409 page.
    /// </summary>
    public ProjectionEvent AppendEvent(string branchId, string type, object payload,
        IReadOnlyList<DependencyRef> deps, IReadOnlyList<string> invalidated,
        string? expectedHead = null, string? actor = null)
    {
        using var tx = C.BeginTransaction();
        string parent; string headIn;
        using (var q = C.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT event_id,head_hash FROM events WHERE branch_id=$b ORDER BY seq DESC LIMIT 1";
            q.Parameters.AddWithValue("$b", branchId);
            using var rr = q.ExecuteReader();
            if (rr.Read()) { parent = rr.GetString(0); headIn = rr.GetString(1); }
            else { parent = "seed"; headIn = "seed"; }
        }
        if (expectedHead is not null && expectedHead != headIn)
        {
            // find the other side's event for the diff
            string their = parent;
            using var find = C.CreateCommand();
            find.Transaction = tx;
            find.CommandText = "SELECT event_id FROM events WHERE branch_id=$b AND head_hash=$h ORDER BY seq DESC LIMIT 1";
            find.Parameters.AddWithValue("$b", branchId);
            find.Parameters.AddWithValue("$h", expectedHead);
            using var fr = find.ExecuteReader();
            string yourEvent = fr.Read() ? fr.GetString(0) : "unknown";
            throw new StaleHeadException
            {
                ExpectedHead = expectedHead, ActualHead = headIn,
                TheirEventId = parent, YourEventId = yourEvent
            };
        }
        int seq;
        using (var sq = C.CreateCommand())
        {
            sq.Transaction = tx;
            sq.CommandText = "SELECT COALESCE(MAX(seq),0)+1 FROM events WHERE branch_id=$b";
            sq.Parameters.AddWithValue("$b", branchId);
            seq = Convert.ToInt32(sq.ExecuteScalar());
        }
        string payloadJson = Hashing.Canonical(payload);
        string depsJson = Hashing.Canonical(deps.OrderBy(d => d.Kind).ThenBy(d => d.ObjectId).ThenBy(d => d.VersionHash));
        string invJson = Hashing.Canonical(invalidated.OrderBy(x => x).ToArray());
        string head = Hashing.Sha256Hex($"{headIn}|{branchId}|{seq}|{type}|{payloadJson}|{depsJson}|{invJson}");
        string eventId = Hashing.ContentId("evt", $"{branchId}|{seq}|{head}");
        string at = IsoNow();
        using var ins = C.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = @"INSERT INTO events(event_id,seq,branch_id,parent_event_id,type,occurred_at,actor,payload_json,dependencies_json,invalidated_json,head_hash)
                            VALUES($e,$s,$b,$p,$t,$at,$actor,$pl,$dep,$inv,$h)";
        ins.Parameters.AddWithValue("$e", eventId);
        ins.Parameters.AddWithValue("$s", seq);
        ins.Parameters.AddWithValue("$b", branchId);
        ins.Parameters.AddWithValue("$p", parent);
        ins.Parameters.AddWithValue("$t", type);
        ins.Parameters.AddWithValue("$at", at);
        ins.Parameters.AddWithValue("$actor", (object?)actor ?? DBNull.Value);
        ins.Parameters.AddWithValue("$pl", payloadJson);
        ins.Parameters.AddWithValue("$dep", depsJson);
        ins.Parameters.AddWithValue("$inv", invJson);
        ins.Parameters.AddWithValue("$h", head);
        ins.ExecuteNonQuery();
        tx.Commit();
        return new ProjectionEvent
        {
            EventId = eventId, Seq = seq, BranchId = branchId, ParentEventId = parent,
            Type = type, OccurredAt = at, Actor = actor,
            Payload = JsonDocument.Parse(payloadJson),
            Dependencies = deps, InvalidatedObjectIds = invalidated, HeadHash = head
        };
    }

    // ---------- decisions ----------
    public void AddDecision(DecisionRecord d)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = @"INSERT INTO decisions(decision_id,branch_id,trial_id,kind,event_id,comment,at,undoes_decision_id,label)
                            VALUES($id,$b,$tr,$k,$e,$c,$at,$u,'adjudication')";
        cmd.Parameters.AddWithValue("$id", d.DecisionId);
        cmd.Parameters.AddWithValue("$b", d.BranchId);
        cmd.Parameters.AddWithValue("$tr", d.TrialId);
        cmd.Parameters.AddWithValue("$k", d.Kind.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$e", d.EventId);
        cmd.Parameters.AddWithValue("$c", (object?)d.Comment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", d.At);
        cmd.Parameters.AddWithValue("$u", (object?)d.UndoesDecisionId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<DecisionRecord> ListDecisions(string branchId)
    {
        var list = new List<DecisionRecord>();
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT decision_id,trial_id,kind,event_id,comment,at,undoes_decision_id FROM decisions WHERE branch_id=$b ORDER BY at";
        cmd.Parameters.AddWithValue("$b", branchId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DecisionRecord
            {
                DecisionId = r.GetString(0), BranchId = branchId, TrialId = r.GetString(1),
                Kind = Enum.Parse<DecisionKind>(r.GetString(2), ignoreCase: true),
                EventId = r.GetString(3),
                Comment = r.IsDBNull(4) ? null : r.GetString(4),
                At = r.GetString(5),
                UndoesDecisionId = r.IsDBNull(6) ? null : r.GetString(6)
            });
        return list;
    }

    public DecisionRecord? LatestDecision(string branchId, string trialId)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT decision_id,kind,event_id,comment,at,undoes_decision_id FROM decisions WHERE branch_id=$b AND trial_id=$t ORDER BY rowid DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$t", trialId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DecisionRecord
        {
            DecisionId = r.GetString(0), BranchId = branchId, TrialId = trialId,
            Kind = Enum.Parse<DecisionKind>(r.GetString(1), ignoreCase: true),
            EventId = r.GetString(2),
            Comment = r.IsDBNull(3) ? null : r.GetString(3),
            At = r.GetString(4),
            UndoesDecisionId = r.IsDBNull(5) ? null : r.GetString(5)
        };
    }

    internal static string IsoNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
}

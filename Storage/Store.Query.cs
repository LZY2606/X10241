using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed record EventRow(string EventId, int Seq, string BranchId, string? AtEvent, string Type,
    string PayloadJson, string RuleVersion, List<string> Deps, List<string> SourceBlobs,
    string CreatedAt, string? SupersededBy, string? SupersededScope);

public sealed record RecordVersionRow(string VersionId, string StableId, int VersionNo, string PayloadJson, string BlobSha);

public sealed partial class Store
{
    public List<EventRow> EventsOnBranch(string branchId)
    {
        var rows = new List<EventRow>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT event_id,seq,branch_id,at_event,type,payload_json,rule_version,deps_json,source_blobs_json,created_at,superseded_by,superseded_scope
                            FROM events WHERE branch_id=@b ORDER BY seq;";
        Add(cmd, "@b", branchId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(ReadEvent(r));
        return rows;
    }

    public EventRow? GetEvent(string eventId)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT event_id,seq,branch_id,at_event,type,payload_json,rule_version,deps_json,source_blobs_json,created_at,superseded_by,superseded_scope
                            FROM events WHERE event_id=@id;";
        Add(cmd, "@id", eventId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEvent(r) : null;
    }

    private static EventRow ReadEvent(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt32(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
        JsonSerializer.Deserialize<List<string>>(r.GetString(7)) ?? new(),
        JsonSerializer.Deserialize<List<string>>(r.GetString(8)) ?? new(),
        r.GetString(9),
        r.IsDBNull(10) ? null : r.GetString(10),
        r.IsDBNull(11) ? null : r.GetString(11));

    public List<RecordVersionRow> Versions(string stableId)
    {
        var rows = new List<RecordVersionRow>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = @"SELECT version_id,stable_id,version_no,payload_json,blob_sha FROM raw_versions
                            WHERE stable_id=@id ORDER BY version_no;";
        Add(cmd, "@id", stableId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new RecordVersionRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
        return rows;
    }

    public string? LatestPayload(string stableId)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM raw_versions WHERE stable_id=@id ORDER BY version_no DESC LIMIT 1;";
        Add(cmd, "@id", stableId);
        return cmd.ExecuteScalar() as string;
    }

    public byte[]? BlobBytes(string sha)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT bytes FROM blobs WHERE sha256=@s;";
        Add(cmd, "@s", sha);
        return cmd.ExecuteScalar() as byte[];
    }

    public List<string> BranchIds()
    {
        var ids = new List<string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id FROM branches ORDER BY created_at;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    }

    public (string id, string label, string? parent, string head)? Branch(string branchId)
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id,label,parent_branch,head_event FROM branches WHERE branch_id=@b;";
        Add(cmd, "@b", branchId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : (string?)r.GetString(3));
    }

    public List<string> ListRecords(string kind)
    {
        var ids = new List<string>();
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT stable_id FROM raw_records WHERE kind=@k ORDER BY stable_id;";
        Add(cmd, "@k", kind);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetString(0));
        return ids;
    }

    /// <summary>回溯到某一事件：返回该分支 seq 不超过目标事件的事件流（当时可见结论与依赖版本）。</summary>
    public List<EventRow> EventsAsOf(string branchId, string eventId)
    {
        var target = GetEvent(eventId) ?? throw new KeyNotFoundException(eventId);
        return EventsOnBranch(branchId).Where(e => e.Seq <= target.Seq).ToList();
    }
}

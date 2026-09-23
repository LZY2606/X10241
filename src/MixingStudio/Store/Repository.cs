using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MixingStudio.Domain;

namespace MixingStudio.Store;

/// <summary>
/// Append-only provenance store. Raw materials, branches, events, candidates and
/// invalidations. Imports are transactional: a failed import rolls back everything.
/// </summary>
public sealed class Repository
{
    private readonly string _path;

    public Repository(string path) => _path = path;

    public SqliteConnection Connect() => Database.Open(_path);

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // ---------- materials ----------

    public void InsertMaterial(SqliteConnection conn, SqliteTransaction tx, MaterialRecord m)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO materials(id,kind,version,raw_json,sha256,event_id) VALUES ($i,$k,$v,$r,$s,$e)";
        cmd.Parameters.AddWithValue("$i", m.Id);
        cmd.Parameters.AddWithValue("$k", m.Kind);
        cmd.Parameters.AddWithValue("$v", m.Version);
        cmd.Parameters.AddWithValue("$r", m.RawJson);
        cmd.Parameters.AddWithValue("$s", m.Sha256);
        cmd.Parameters.AddWithValue("$e", m.EventId);
        cmd.ExecuteNonQuery();
    }

    public List<MaterialRecord> Materials(string? kind = null)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = kind is null
            ? "SELECT id,kind,version,raw_json,sha256,event_id FROM materials ORDER BY id, version"
            : "SELECT id,kind,version,raw_json,sha256,event_id FROM materials WHERE kind=$k ORDER BY id, version";
        if (kind is not null) cmd.Parameters.AddWithValue("$k", kind);
        var list = new List<MaterialRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaterialRecord(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        return list;
    }

    public MaterialRecord? Material(string id, int version)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,kind,version,raw_json,sha256,event_id FROM materials WHERE id=$i AND version=$v";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.Parameters.AddWithValue("$v", version);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new MaterialRecord(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5))
            : null;
    }

    public MaterialRecord? LatestMaterial(string id)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,kind,version,raw_json,sha256,event_id FROM materials WHERE id=$i ORDER BY version DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$i", id);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new MaterialRecord(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5))
            : null;
    }

    // ---------- branches ----------

    public void InsertBranch(SqliteConnection conn, SqliteTransaction tx, BranchRecord b)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO branches(id,name,parent_branch_id,base_event_id,head_seq) VALUES ($i,$n,$p,$b,$h)";
        cmd.Parameters.AddWithValue("$i", b.Id);
        cmd.Parameters.AddWithValue("$n", b.Name);
        cmd.Parameters.AddWithValue("$p", (object?)b.ParentBranchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", b.HeadSeq);
        cmd.Parameters.AddWithValue("$b", b.BaseEventId);
        cmd.ExecuteNonQuery();
    }

    public List<BranchRecord> Branches()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,parent_branch_id,base_event_id,head_seq FROM branches ORDER BY id";
        var list = new List<BranchRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BranchRecord(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetInt64(4)));
        return list;
    }

    public BranchRecord? Branch(string id) => Branches().FirstOrDefault(b => b.Id == id);

    public void UpdateBranchHead(SqliteConnection conn, SqliteTransaction tx, string branchId, long headSeq)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE branches SET head_seq=$h WHERE id=$i";
        cmd.Parameters.AddWithValue("$h", headSeq);
        cmd.Parameters.AddWithValue("$i", branchId);
        cmd.ExecuteNonQuery();
    }

    // ---------- events ----------

    public void InsertEvent(SqliteConnection conn, SqliteTransaction tx, EventRecord e)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO events(id,branch_id,seq,type,payload_json,material_refs_json,created_at) VALUES ($i,$b,$s,$t,$p,$m,$c)";
        cmd.Parameters.AddWithValue("$i", e.Id);
        cmd.Parameters.AddWithValue("$b", e.BranchId);
        cmd.Parameters.AddWithValue("$s", e.Seq);
        cmd.Parameters.AddWithValue("$t", e.Type);
        cmd.Parameters.AddWithValue("$p", e.PayloadJson);
        cmd.Parameters.AddWithValue("$m", e.MaterialRefsJson);
        cmd.Parameters.AddWithValue("$c", e.CreatedAt);
        cmd.ExecuteNonQuery();
    }

    public List<EventRecord> Events(string? branchId = null)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = branchId is null
            ? "SELECT id,branch_id,seq,type,payload_json,material_refs_json,created_at FROM events ORDER BY seq"
            : "SELECT id,branch_id,seq,type,payload_json,material_refs_json,created_at FROM events WHERE branch_id=$b ORDER BY seq";
        if (branchId is not null) cmd.Parameters.AddWithValue("$b", branchId);
        var list = new List<EventRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EventRecord(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)));
        return list;
    }

    public EventRecord? Event(string id) => Events().FirstOrDefault(e => e.Id == id);

    public long NextSeq()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(seq),0)+1 FROM events";
        return (long)cmd.ExecuteScalar()!;
    }

    // ---------- candidates ----------

    public void InsertCandidate(SqliteConnection conn, SqliteTransaction tx, CandidateRecord c)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO candidates(id,event_id,branch_id,region_index,polygon_json,status,adjudication_event_id) VALUES ($i,$e,$b,$r,$p,$s,$a)";
        cmd.Parameters.AddWithValue("$i", c.Id);
        cmd.Parameters.AddWithValue("$e", c.EventId);
        cmd.Parameters.AddWithValue("$b", c.BranchId);
        cmd.Parameters.AddWithValue("$r", c.RegionIndex);
        cmd.Parameters.AddWithValue("$p", c.PolygonJson);
        cmd.Parameters.AddWithValue("$s", c.Status);
        cmd.Parameters.AddWithValue("$a", (object?)c.AdjudicationEventId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<CandidateRecord> Candidates(string? branchId = null)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = branchId is null
            ? "SELECT id,event_id,region_index,polygon_json,status,adjudication_event_id FROM candidates ORDER BY id"
            : "SELECT id,event_id,region_index,polygon_json,status,adjudication_event_id FROM candidates WHERE branch_id=$b ORDER BY id";
        if (branchId is not null) cmd.Parameters.AddWithValue("$b", branchId);
        var list = new List<CandidateRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CandidateRecord(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    public void SetCandidateStatus(SqliteConnection conn, SqliteTransaction tx, string candidateId, string status, string adjudicationEventId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE candidates SET status=$s, adjudication_event_id=$a WHERE id=$i";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$a", adjudicationEventId);
        cmd.Parameters.AddWithValue("$i", candidateId);
        cmd.ExecuteNonQuery();
    }

    // ---------- invalidations ----------

    public void InsertInvalidation(SqliteConnection conn, SqliteTransaction tx, InvalidationRecord inv)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO invalidations(id,event_id,target_type,target_id,reason) VALUES ($i,$e,$tt,$ti,$r)";
        cmd.Parameters.AddWithValue("$i", inv.Id);
        cmd.Parameters.AddWithValue("$e", inv.EventId);
        cmd.Parameters.AddWithValue("$tt", inv.TargetType);
        cmd.Parameters.AddWithValue("$ti", inv.TargetId);
        cmd.Parameters.AddWithValue("$r", inv.Reason);
        cmd.ExecuteNonQuery();
    }

    public List<InvalidationRecord> Invalidations()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,event_id,target_type,target_id,reason FROM invalidations ORDER BY id";
        var list = new List<InvalidationRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new InvalidationRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }
}

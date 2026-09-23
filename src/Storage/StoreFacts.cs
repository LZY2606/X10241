using System.Text.Json.Nodes;
using IsotopeMix.Model;
using Microsoft.Data.Sqlite;

namespace IsotopeMix.Storage;

public sealed partial class Store
{
    public int AddFact(SqliteConnection conn, SqliteTransaction tx, FactRecord rec)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO fact_record
            (stable_id,kind,version,batch_id,blob_id,source_ref,created_at,payload_json)
            VALUES ($sid,$kind,$ver,$batch,$blob,$ref,$at,$payload);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$sid", rec.StableId);
        cmd.Parameters.AddWithValue("$kind", rec.Kind);
        cmd.Parameters.AddWithValue("$ver", rec.Version);
        cmd.Parameters.AddWithValue("$batch", rec.BatchId);
        cmd.Parameters.AddWithValue("$blob", rec.BlobId);
        cmd.Parameters.AddWithValue("$ref", rec.SourceRef);
        cmd.Parameters.AddWithValue("$at", rec.CreatedAt);
        cmd.Parameters.AddWithValue("$payload", JsonIo.Canonical(rec.Payload));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int NextVersion(SqliteConnection conn, SqliteTransaction tx, string stableId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(version),0)+1 FROM fact_record WHERE stable_id=$id";
        cmd.Parameters.AddWithValue("$id", stableId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public FactRecord? GetFactAt(string stableId, string kind, string atEventId)
    {
        using var conn = _db.Open();
        return GetFactAt(conn, null, stableId, kind, atEventId);
    }

    // Latest version of a fact that was visible at the given event. Facts are
    // globally visible (shared raw layer), but versions newer than the event
    // are hidden so historical views are not overwritten.
    public FactRecord? GetFactAt(SqliteConnection conn, SqliteTransaction? tx,
        string stableId, string kind, string atEventId)
    {
        long seq = EventSeq(conn, tx, atEventId);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT f.row_id,f.stable_id,f.kind,f.version,f.batch_id,f.blob_id,
              f.source_ref,f.created_at,f.payload_json
            FROM fact_record f
            JOIN event_record e ON e.seq >= f.rowid AND e.event_id=$at
            WHERE f.stable_id=$sid AND f.kind=$kind
            ORDER BY f.version DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$sid", stableId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$seq", seq);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return ReadFact(r);
    }

    public List<FactRecord> ListFactsAt(string atEventId, string? kind = null)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        string kindFilter = kind is null ? "" : "AND kind=$kind";
        cmd.CommandText = $@"WITH atpt AS (SELECT seq FROM event_record WHERE event_id=$at)
            SELECT * FROM (
              SELECT f.*, ROW_NUMBER() OVER (PARTITION BY f.stable_id ORDER BY f.version DESC) rn
              FROM fact_record f, atpt
              WHERE f.row_id <= atpt.seq {kindFilter}
            ) WHERE rn=1 ORDER BY stable_id";
        cmd.Parameters.AddWithValue("$at", atEventId);
        if (kind is not null) cmd.Parameters.AddWithValue("$kind", kind);
        var result = new List<FactRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
                result.Add(ReadFactFromRow(r));
        }
        return result;
    }

    public List<FactRecord> ListAllFacts(string? kind = null)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM fact_record" + (kind is null ? "" : " WHERE kind=$kind") +
                          " ORDER BY stable_id, version";
        if (kind is not null) cmd.Parameters.AddWithValue("$kind", kind);
        var result = new List<FactRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(ReadFactFromRow(r));
        return result;
    }

    private static FactRecord ReadFact(SqliteDataReader r) => new()
    {
        RowId = r.GetInt64(0),
        StableId = r.GetString(1),
        Kind = r.GetString(2),
        Version = r.GetInt32(3),
        BatchId = r.GetString(4),
        BlobId = r.GetString(5),
        SourceRef = r.GetString(6),
        CreatedAt = r.GetString(7),
        Payload = JsonIo.ParseObject(r.GetString(8))
    };

    private static FactRecord ReadFactFromRow(SqliteDataReader r)
    {
        // Windowed query columns: row_id, stable_id, kind, version, ...
        return new FactRecord
        {
            RowId = r.GetInt64(r.GetOrdinal("row_id")),
            StableId = r.GetString(r.GetOrdinal("stable_id")),
            Kind = r.GetString(r.GetOrdinal("kind")),
            Version = r.GetInt32(r.GetOrdinal("version")),
            BatchId = r.GetString(r.GetOrdinal("batch_id")),
            BlobId = r.GetString(r.GetOrdinal("blob_id")),
            SourceRef = r.GetString(r.GetOrdinal("source_ref")),
            CreatedAt = r.GetString(r.GetOrdinal("created_at")),
            Payload = JsonIo.ParseObject(r.GetString(r.GetOrdinal("payload_json")))
        };
    }

    private static long EventSeq(SqliteConnection conn, SqliteTransaction? tx, string eventId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT seq FROM event_record WHERE event_id=$id";
        cmd.Parameters.AddWithValue("$id", eventId);
        var v = cmd.ExecuteScalar();
        return v is null ? long.MaxValue : Convert.ToInt64(v);
    }
}

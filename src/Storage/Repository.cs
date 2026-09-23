using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed class Repository
{
    private readonly Database _db;
    public Repository(Database db) { _db = db; }

    public int SchemaVersion()
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_info';";
        if (cmd.ExecuteScalar() is null) return 0;
        cmd.CommandText = "SELECT MAX(version) FROM schema_info;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public string CreateImportBatch(string batchId, string purpose, JsonNode manifest)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"INSERT INTO import_batches(batch_id, created_at, purpose, manifest_json)
                            VALUES($id,$t,$p,$m);";
        cmd.Parameters.AddWithValue("$id", batchId);
        cmd.Parameters.AddWithValue("$t", Clock());
        cmd.Parameters.AddWithValue("$p", purpose);
        cmd.Parameters.AddWithValue("$m", manifest.ToJsonString());
        cmd.ExecuteNonQuery();
        return batchId;
    }

    /// <summary>Atomic import: every record commits together; any failure rolls back the whole batch.</summary>
    public void CommitBatch(string batchId, IReadOnlyList<RawRecordRow> rows)
    {
        using var cn = _db.Open();
        using var tx = cn.BeginTransaction();
        try
        {
            foreach (var r in rows)
            {
                using var cmd = cn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO raw_records(uid,batch_id,record_kind,ordinal,raw_json,raw_text,
                                        unit,datum,byte_sha256,parse_status,diagnostics_json)
                                    VALUES($uid,$b,$k,$o,$rj,$rt,$u,$d,$h,$s,$diag);";
                cmd.Parameters.AddWithValue("$uid", r.Uid);
                cmd.Parameters.AddWithValue("$b", r.BatchId);
                cmd.Parameters.AddWithValue("$k", r.RecordKind);
                cmd.Parameters.AddWithValue("$o", r.Ordinal);
                cmd.Parameters.AddWithValue("$rj", r.RawJson);
                cmd.Parameters.AddWithValue("$rt", r.RawText);
                cmd.Parameters.AddWithValue("$u", (object?)r.Unit ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$d", (object?)r.Datum ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$h", r.ByteSha256);
                cmd.Parameters.AddWithValue("$s", r.ParseStatus);
                cmd.Parameters.AddWithValue("$diag", r.DiagnosticsJson);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            RemoveBatchIfPresent(batchId);
            throw;
        }
    }

    private void RemoveBatchIfPresent(string batchId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM raw_records WHERE batch_id=$b; DELETE FROM import_batches WHERE batch_id=$b;";
        cmd.Parameters.AddWithValue("$b", batchId);
        cmd.ExecuteNonQuery();
    }

    public int CountBatches()
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM import_batches;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<(string kind, string uid, string raw, string parseStatus, string diagnostics)> ReadRawByBatch(string batchId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT record_kind,uid,raw_json,parse_status,diagnostics_json
                            FROM raw_records WHERE batch_id=$b ORDER BY ordinal;";
        cmd.Parameters.AddWithValue("$b", batchId);
        using var rd = cmd.ExecuteReader();
        var list = new List<(string, string, string, string, string)>();
        while (rd.Read())
            list.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4)));
        return list;
    }

    public List<string> AllBatches()
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT batch_id FROM import_batches ORDER BY created_at;";
        var list = new List<string>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(rd.GetString(0));
        return list;
    }

    public string? GetRawText(string uid)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT raw_json FROM raw_records WHERE uid=$u ORDER BY ordinal LIMIT 1;";
        cmd.Parameters.AddWithValue("$u", uid);
        return cmd.ExecuteScalar() as string;
    }

    public List<RawRecordRow> AllRawRecords()
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT uid,batch_id,record_kind,ordinal,raw_json,raw_text,unit,datum,
                                   byte_sha256,parse_status,diagnostics_json
                            FROM raw_records ORDER BY batch_id,ordinal;";
        using var rd = cmd.ExecuteReader();
        var list = new List<RawRecordRow>();
        while (rd.Read())
        {
            list.Add(new RawRecordRow
            {
                Uid = rd.GetString(0), BatchId = rd.GetString(1), RecordKind = rd.GetString(2),
                Ordinal = rd.GetInt32(3), RawJson = rd.GetString(4), RawText = rd.GetString(5),
                Unit = rd.IsDBNull(6) ? null : rd.GetString(6),
                Datum = rd.IsDBNull(7) ? null : rd.GetString(7),
                ByteSha256 = rd.GetString(8), ParseStatus = rd.GetString(9), DiagnosticsJson = rd.GetString(10)
            });
        }
        return list;
    }

    public static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Clock() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
}

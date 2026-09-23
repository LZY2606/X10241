using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsotopeMix.Infrastructure;

public sealed class Store : IDisposable
{
    private readonly SqliteConnection _conn;
    public string DatabasePath { get; }

    public Store(string path)
    {
        DatabasePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString());
        _conn.Open();
        Migrate();
    }

    public SqliteConnection Connection => _conn;

    private void Migrate()
    {
        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        int current = 0;
        try
        {
            using var q = _conn.CreateCommand();
            q.CommandText = "SELECT MAX(version) FROM schema_version";
            var v = q.ExecuteScalar();
            if (v != null && v != DBNull.Value) current = Convert.ToInt32(v);
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { /* 全新库，表尚不存在 */ }

        foreach (var migration in Schema.Migrations.Where(m => m.Version > current))
        {
            using var tx = _conn.BeginTransaction();
            foreach (var stmt in migration.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                cmd.ExecuteNonQuery();
            }
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO schema_version(version, applied_at) VALUES($v, $t)";
            ins.Parameters.AddWithValue("$v", migration.Version);
            ins.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
            ins.ExecuteNonQuery();
            tx.Commit();
        }
    }

    public int SchemaVersion()
    {
        using var q = _conn.CreateCommand();
        q.CommandText = "SELECT MAX(version) FROM schema_version";
        return Convert.ToInt32(q.ExecuteScalar());
    }

    public static string NewId(string prefix)
        => $"{prefix}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{RandomNumberGenerator.GetInt32(0, 0xFFFFFF):X6}";

    public static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public string PutBlob(byte[] bytes, string mediaType)
    {
        string hash = Sha256(bytes);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO blobs(sha256, media_type, bytes) VALUES($h, $m, $b)
                            ON CONFLICT(sha256) DO NOTHING";
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$m", mediaType);
        cmd.Parameters.AddWithValue("$b", bytes);
        cmd.ExecuteNonQuery();
        return hash;
    }

    public byte[]? GetBlob(string sha)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT bytes FROM blobs WHERE sha256=$h";
        cmd.Parameters.AddWithValue("$h", sha);
        return cmd.ExecuteScalar() as byte[];
    }

    public void InsertRaw(string caseId, string kind, string stableId, string originalJson,
        string? blobSha, string? branchId, SqliteTransaction? tx = null)
    {
        var conn = tx?.Connection ?? _conn;
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO raw_records(raw_id, case_id, kind, stable_id, original_json,
            blob_sha256, source_branch_id, created_at)
            VALUES($id,$c,$k,$s,$j,$b,$br,$t)";
        cmd.Parameters.AddWithValue("$id", NewId("raw"));
        cmd.Parameters.AddWithValue("$c", caseId);
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$s", stableId);
        cmd.Parameters.AddWithValue("$j", originalJson);
        cmd.Parameters.AddWithValue("$b", (object?)blobSha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$br", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<RawRow> GetRaws(string caseId, string? kind = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT raw_id, case_id, kind, stable_id, original_json, blob_sha256, source_branch_id, created_at "
            + "FROM raw_records WHERE case_id=$c " + (kind is null ? "" : "AND kind=$k ") + "ORDER BY rowid";
        cmd.Parameters.AddWithValue("$c", caseId);
        if (kind is not null) cmd.Parameters.AddWithValue("$k", kind);
        var list = new List<RawRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new RawRow
            {
                RawId = rd.GetString(0), CaseId = rd.GetString(1), Kind = rd.GetString(2),
                StableId = rd.GetString(3), OriginalJson = rd.GetString(4),
                BlobSha256 = rd.IsDBNull(5) ? null : rd.GetString(5),
                SourceBranchId = rd.IsDBNull(6) ? null : rd.GetString(6),
                CreatedAt = rd.GetString(7)
            });
        return list;
    }

    public void CreateBranch(string branchId, string caseId, string name, string? parentBranchId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO branches(branch_id, case_id, name, parent_branch_id, head_seq)
            VALUES($b,$c,$n,$p, COALESCE((SELECT head_seq FROM branches WHERE branch_id=$p),0))";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$c", caseId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", (object?)parentBranchId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public BranchRow? GetBranch(string caseId, string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id, case_id, name, COALESCE(parent_branch_id,''), head_seq FROM branches WHERE case_id=$c AND name=$n";
        cmd.Parameters.AddWithValue("$c", caseId);
        cmd.Parameters.AddWithValue("$n", name);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new BranchRow
        {
            BranchId = rd.GetString(0), CaseId = rd.GetString(1), Name = rd.GetString(2),
            ParentBranchId = rd.GetString(3), HeadSeq = rd.GetInt64(4)
        };
    }

    public List<BranchRow> ListBranches(string caseId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT branch_id, case_id, name, COALESCE(parent_branch_id,''), head_seq FROM branches WHERE case_id=$c ORDER BY rowid";
        cmd.Parameters.AddWithValue("$c", caseId);
        var list = new List<BranchRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new BranchRow
            {
                BranchId = rd.GetString(0), CaseId = rd.GetString(1), Name = rd.GetString(2),
                ParentBranchId = rd.GetString(3), HeadSeq = rd.GetInt64(4)
            });
        return list;
    }

    public SqliteTransaction BeginTransaction() => _conn.BeginTransaction();

    public sealed class BranchConflictException : Exception
    {
        public long ExpectedHead { get; }
        public long ActualHead { get; }
        public EventRow? TheirEvent { get; }
        public BranchConflictException(long expected, long actual, EventRow? theirs)
            : base($"BRANCH_CONFLICT: 期望基线序号 {expected}，分支当前序号 {actual}")
        { ExpectedHead = expected; ActualHead = actual; TheirEvent = theirs; }
    }

    /// <summary>
    /// 在分支上追加事件；expectedHeadSeq 与当前不一致时抛冲突（两窗口并发保存）。
    /// 同一事务内更新 head，保证原子性。
    /// </summary>
    public EventRow AppendEvent(string caseId, string branchId, string type, object payload,
        long expectedHeadSeq, string? dependsOnEventId = null, string? basisEventId = null,
        string author = "local", SqliteTransaction? externalTx = null)
    {
        var ownTx = externalTx is null;
        var tx = externalTx ?? _conn.BeginTransaction();
        try
        {
            long actual = HeadSeq(branchId, tx);
            if (actual != expectedHeadSeq)
            {
                var theirs = LatestEvent(branchId, tx);
                throw new BranchConflictException(expectedHeadSeq, actual, theirs);
            }
            var row = new EventRow
            {
                EventId = NewId("evt"),
                CaseId = caseId,
                BranchId = branchId,
                Type = type,
                PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                DependsOnEventId = dependsOnEventId,
                BasisEventId = basisEventId,
                Author = author,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            long seq;
            using (var ins = _conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = @"INSERT INTO events(event_id, case_id, branch_id, type, payload_json,
                    depends_on_event_id, basis_event_id, author, created_at)
                    VALUES($id,$c,$b,$t,$p,$d,$bs,$a,$ts); SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("$id", row.EventId);
                ins.Parameters.AddWithValue("$c", caseId);
                ins.Parameters.AddWithValue("$b", branchId);
                ins.Parameters.AddWithValue("$t", type);
                ins.Parameters.AddWithValue("$p", row.PayloadJson);
                ins.Parameters.AddWithValue("$d", (object?)dependsOnEventId ?? DBNull.Value);
                ins.Parameters.AddWithValue("$bs", (object?)basisEventId ?? DBNull.Value);
                ins.Parameters.AddWithValue("$a", author);
                ins.Parameters.AddWithValue("$ts", row.CreatedAt);
                seq = Convert.ToInt64(ins.ExecuteScalar());
            }
            using (var upd = _conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE branches SET head_seq=$h WHERE branch_id=$b";
                upd.Parameters.AddWithValue("$h", seq);
                upd.Parameters.AddWithValue("$b", branchId);
                upd.ExecuteNonQuery();
            }
            row.Seq = seq;
            if (ownTx) tx.Commit();
            return row;
        }
        catch
        {
            if (ownTx) tx.Rollback();
            throw;
        }
        finally
        {
            if (ownTx) tx.Dispose();
        }
    }

    private long HeadSeq(string branchId, SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT head_seq FROM branches WHERE branch_id=$b";
        cmd.Parameters.AddWithValue("$b", branchId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private EventRow? LatestEvent(string branchId, SqliteTransaction tx)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT seq, event_id, case_id, branch_id, type, payload_json,
            COALESCE(depends_on_event_id,''), COALESCE(basis_event_id,''), author, created_at
            FROM events WHERE branch_id=$b ORDER BY seq DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branchId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new EventRow
        {
            Seq = rd.GetInt64(0), EventId = rd.GetString(1), CaseId = rd.GetString(2),
            BranchId = rd.GetString(3), Type = rd.GetString(4), PayloadJson = rd.GetString(5),
            DependsOnEventId = rd.GetString(6), BasisEventId = rd.GetString(7),
            Author = rd.GetString(8), CreatedAt = rd.GetString(9)
        };
    }

    public List<EventRow> EventsOnBranch(string branchId, long? upToSeq = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT seq, event_id, case_id, branch_id, type, payload_json,
            COALESCE(depends_on_event_id,''), COALESCE(basis_event_id,''), author, created_at
            FROM events WHERE branch_id=$b " + (upToSeq is null ? "" : "AND seq<=$s ") + "ORDER BY seq";
        cmd.Parameters.AddWithValue("$b", branchId);
        if (upToSeq is long s) cmd.Parameters.AddWithValue("$s", s);
        var list = new List<EventRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new EventRow
            {
                Seq = rd.GetInt64(0), EventId = rd.GetString(1), CaseId = rd.GetString(2),
                BranchId = rd.GetString(3), Type = rd.GetString(4), PayloadJson = rd.GetString(5),
                DependsOnEventId = rd.GetString(6), BasisEventId = rd.GetString(7),
                Author = rd.GetString(8), CreatedAt = rd.GetString(9)
            });
        return list;
    }

    public EventRow? GetEvent(string eventId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT seq, event_id, case_id, branch_id, type, payload_json,
            COALESCE(depends_on_event_id,''), COALESCE(basis_event_id,''), author, created_at
            FROM events WHERE event_id=$id";
        cmd.Parameters.AddWithValue("$id", eventId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new EventRow
        {
            Seq = rd.GetInt64(0), EventId = rd.GetString(1), CaseId = rd.GetString(2),
            BranchId = rd.GetString(3), Type = rd.GetString(4), PayloadJson = rd.GetString(5),
            DependsOnEventId = rd.GetString(6), BasisEventId = rd.GetString(7),
            Author = rd.GetString(8), CreatedAt = rd.GetString(9)
        };
    }

    public List<string> ListCases()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT case_id FROM events WHERE type=$t ORDER BY case_id";
        cmd.Parameters.AddWithValue("$t", EventTypes.CaseImported);
        var list = new List<string>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) list.Add(rd.GetString(0));
        return list;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Dispose() => _conn.Dispose();
}

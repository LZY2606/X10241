using Microsoft.Data.Sqlite;

namespace IsotopeMix.Domain;

public sealed class StoredEvent
{
    public long Seq { get; set; }
    public string EventId { get; set; } = "";
    public string Type { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public string At { get; set; } = "";
}

public sealed class EventStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private long _seq;

    public EventStore(string connectionString)
    {
        _conn = new SqliteConnection(connectionString);
        _conn.Open();
        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        Migrate();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(seq),0) FROM events;";
        _seq = (long)cmd.ExecuteScalar()!;
    }

    public SqliteConnection Underlying => _conn;

    public static int CurrentSchema => 2;

    private void Migrate()
    {
        int userVersion;
        using (var v = _conn.CreateCommand())
        {
            v.CommandText = "PRAGMA user_version;";
            userVersion = Convert.ToInt32(v.ExecuteScalar());
        }
        if (userVersion < 1)
        {
            Exec(@"
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE blobs(
  blob_id TEXT PRIMARY KEY,
  media_type TEXT NOT NULL,
  bytes BLOB NOT NULL,
  sha256 TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE events(
  seq INTEGER PRIMARY KEY AUTOINCREMENT,
  event_id TEXT NOT NULL UNIQUE,
  type TEXT NOT NULL,
  branch_id TEXT NOT NULL DEFAULT '',
  payload TEXT NOT NULL,
  at TEXT NOT NULL
);
CREATE INDEX ix_events_branch ON events(branch_id);");
        }
        if (userVersion < 2)
        {
            Exec("ALTER TABLE blobs ADD COLUMN origin TEXT NOT NULL DEFAULT '';");
        }
        using var set = _conn.CreateCommand();
        set.CommandText = $"PRAGMA user_version = {CurrentSchema};";
        set.ExecuteNonQuery();
    }

    public int SchemaVersion()
    {
        using var v = _conn.CreateCommand();
        v.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(v.ExecuteScalar());
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public List<StoredEvent> AllEvents(long? afterSeq = null)
    {
        var list = new List<StoredEvent>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = afterSeq.HasValue
            ? "SELECT seq,event_id,type,branch_id,payload,at FROM events WHERE seq>$s ORDER BY seq"
            : "SELECT seq,event_id,type,branch_id,payload,at FROM events ORDER BY seq";
        if (afterSeq.HasValue) { cmd.Parameters.AddWithValue("$s", afterSeq.Value); }
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StoredEvent
            {
                Seq = r.GetInt64(0),
                EventId = r.GetString(1),
                Type = r.GetString(2),
                BranchId = r.GetString(3),
                PayloadJson = r.GetString(4),
                At = r.GetString(5)
            });
        return list;
    }

    public byte[]? GetBlob(string blobId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT bytes FROM blobs WHERE blob_id=$id";
        cmd.Parameters.AddWithValue("$id", blobId);
        return cmd.ExecuteScalar() as byte[];
    }

    public void PutBlob(string blobId, string mediaType, byte[] bytes, string sha256, string at, string origin)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO blobs(blob_id,media_type,bytes,sha256,created_at,origin)
VALUES($id,$m,$b,$s,$t,$o)";
        cmd.Parameters.AddWithValue("$id", blobId);
        cmd.Parameters.AddWithValue("$m", mediaType);
        cmd.Parameters.AddWithValue("$b", bytes);
        cmd.Parameters.AddWithValue("$s", sha256);
        cmd.Parameters.AddWithValue("$t", at);
        cmd.Parameters.AddWithValue("$o", origin);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 在单个事务内追加 blob 与事件；任一写入失败则整体回滚，不留半份记录。
    /// </summary>
    public long AppendInTransaction(IEnumerable<BlobWrite> blobs, IEnumerable<EventWrite> events)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            foreach (var blob in blobs)
            {
                using var bc = _conn.CreateCommand();
                bc.Transaction = tx;
                bc.CommandText = @"INSERT OR IGNORE INTO blobs(blob_id,media_type,bytes,sha256,created_at,origin)
VALUES($id,$m,$b,$s,$t,$o)";
                bc.Parameters.AddWithValue("$id", blob.BlobId);
                bc.Parameters.AddWithValue("$m", blob.MediaType);
                bc.Parameters.AddWithValue("$b", blob.Bytes);
                bc.Parameters.AddWithValue("$s", blob.Sha256);
                bc.Parameters.AddWithValue("$t", blob.At);
                bc.Parameters.AddWithValue("$o", blob.Origin);
                bc.ExecuteNonQuery();
            }
            long last = 0;
            foreach (var ev in events)
            {
                using var ec = _conn.CreateCommand();
                ec.Transaction = tx;
                ec.CommandText = @"INSERT INTO events(event_id,type,branch_id,payload,at)
VALUES($id,$t,$b,$p,$at)";
                ec.Parameters.AddWithValue("$id", ev.EventId);
                ec.Parameters.AddWithValue("$t", ev.Type);
                ec.Parameters.AddWithValue("$b", ev.BranchId);
                ec.Parameters.AddWithValue("$p", ev.PayloadJson);
                ec.Parameters.AddWithValue("$at", ev.At);
                ec.ExecuteNonQuery();
                using var lid = _conn.CreateCommand();
                lid.Transaction = tx;
                lid.CommandText = "SELECT last_insert_rowid()";
                last = (long)lid.ExecuteScalar()!;
            }
            tx.Commit();
            _seq = last;
            return last;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void Dispose() => _conn.Dispose();
}

public sealed record BlobWrite(string BlobId, string MediaType, byte[] Bytes, string Sha256, string At, string Origin);
public sealed record EventWrite(string EventId, string Type, string BranchId, string PayloadJson, string At);

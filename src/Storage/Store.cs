using System.Text.Json.Nodes;
using IsotopeMix.Model;
using Microsoft.Data.Sqlite;

namespace IsotopeMix.Storage;

// All writes to blobs/facts/events/branches go through here, in transactions.
// Import failures roll back the whole transaction so no half records remain.
public sealed partial class Store
{
    private readonly Database _db;
    public Store(Database db)
    {
        _db = db;
        using var conn = _db.Open();
        Migrations.Migrate(conn);
    }

    public Database Database => _db;

    // ----- Raw blobs -------------------------------------------------------
    public string PutBlobIfAbsent(string fileName, string mediaType, byte[] content, string createdAt)
    {
        using var conn = _db.Open();
        return PutBlobIfAbsent(conn, null, fileName, mediaType, content, createdAt);
    }

    public string PutBlobIfAbsent(SqliteConnection conn, SqliteTransaction? tx, string fileName,
        string mediaType, byte[] content, string createdAt)
    {
        string blobId = "blob-" + JsonIo.Sha256Hex(content);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO raw_blob(blob_id,file_name,media_type,content,created_at)" +
                          " VALUES ($id,$f,$m,$c,$t)";
        cmd.Parameters.AddWithValue("$id", blobId);
        cmd.Parameters.AddWithValue("$f", fileName);
        cmd.Parameters.AddWithValue("$m", mediaType);
        cmd.Parameters.AddWithValue("$c", content);
        cmd.Parameters.AddWithValue("$t", createdAt);
        cmd.ExecuteNonQuery();
        return blobId;
    }

    public RawBlob? GetBlob(string blobId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT blob_id,file_name,media_type,content FROM raw_blob WHERE blob_id=$id";
        cmd.Parameters.AddWithValue("$id", blobId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new RawBlob
        {
            BlobId = r.GetString(0),
            FileName = r.GetString(1),
            MediaType = r.GetString(2),
            Content = (byte[])r[3]
        };
    }

    public List<RawBlob> ListBlobs()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT blob_id,file_name,media_type,content FROM raw_blob ORDER BY blob_id";
        var result = new List<RawBlob>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new RawBlob { BlobId = r.GetString(0), FileName = r.GetString(1),
                MediaType = r.GetString(2), Content = (byte[])r[3] });
        return result;
    }

    // ----- Batches ---------------------------------------------------------
    public void PutBatch(ImportBatch batch)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        PutBatch(conn, tx, batch);
        tx.Commit();
    }

    public void PutBatch(SqliteConnection conn, SqliteTransaction tx, ImportBatch batch)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO import_batch(batch_id,label,created_at) VALUES($id,$l,$t)";
        cmd.Parameters.AddWithValue("$id", batch.BatchId);
        cmd.Parameters.AddWithValue("$l", batch.Label);
        cmd.Parameters.AddWithValue("$t", batch.CreatedAt);
        cmd.ExecuteNonQuery();
    }

    public List<ImportBatch> ListBatches()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT batch_id,label,created_at FROM import_batch ORDER BY batch_id";
        var result = new List<ImportBatch>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Add(new ImportBatch { BatchId = r.GetString(0), Label = r.GetString(1), CreatedAt = r.GetString(2) });
        return result;
    }
}

using System.Text.Json;
using IsotopeApp.Domain;
using Microsoft.Data.Sqlite;

namespace IsotopeApp.Storage;

/// <summary>Persistence for imported raw data, versioned interpretations and trials.</summary>
public sealed partial class Store
{
    public sealed record ImportedBatch(string BatchId, IReadOnlyList<ImportDiagnostic> Diagnostics);

    /// <summary>
    /// Insert one import batch as a single transaction. Any failure rolls the
    /// whole batch back: no half samples, no orphan covariance rows.
    /// </summary>
    public ImportedBatch ImportBatch(string branchId, byte[] rawBytes, string mediaType,
        JsonDocument parsed, IEnumerable<object> rows)
    {
        var diags = new List<ImportDiagnostic>();
        using var tx = C.BeginTransaction();
        string blobId = Hashing.ContentId("blob", Hashing.Sha256Hex(rawBytes) + mediaType);
        string batchId = Hashing.ContentId("batch", blobId + branchId + IsoNow());
        try
        {
            InsertBlob(tx, blobId, rawBytes, mediaType);
            using (var b = C.CreateCommand())
            {
                b.Transaction = tx;
                b.CommandText = "INSERT INTO import_batches(batch_id,raw_blob_id,status,imported_at,diagnostics_json) VALUES($id,$blob,'imported',$t,'[]')";
                b.Parameters.AddWithValue("$id", batchId);
                b.Parameters.AddWithValue("$blob", blobId);
                b.Parameters.AddWithValue("$t", IsoNow());
                b.ExecuteNonQuery();
            }
            foreach (var row in rows)
            {
                switch (row)
                {
                    case SampleRecord sample: InsertSample(tx, branchId, batchId, sample); break;
                    case SourceRecord source: InsertSource(tx, branchId, batchId, source); break;
                    case MeasurementCovariance cov when cov is not null:
                        throw new InvalidOperationException("covariance persisted with sample");
                    case ImportDiagnostic d: diags.Add(d); break;
                }
            }
            using (var u = C.CreateCommand())
            {
                u.Transaction = tx;
                u.CommandText = "UPDATE import_batches SET diagnostics_json=$d WHERE batch_id=$id";
                u.Parameters.AddWithValue("$d", Hashing.Canonical(diags));
                u.Parameters.AddWithValue("$id", batchId);
                u.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        return new ImportedBatch(batchId, diags);
    }

    private void InsertBlob(SqliteTransaction tx, string id, byte[] bytes, string mediaType)
    {
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO raw_blobs(blob_id,sha256,media_type,bytes,imported_at) VALUES($id,$h,$m,$b,$t)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$h", Hashing.Sha256Hex(bytes));
        cmd.Parameters.AddWithValue("$m", mediaType);
        cmd.Parameters.AddWithValue("$b", bytes);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
    }

    private void InsertSample(SqliteTransaction tx, string branch, string batch, SampleRecord s)
    {
        string version = Hashing.ContentId("ver", s.RawJson);
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR IGNORE INTO samples(sample_id,version_hash,batch_id,branch_id,supersedes_version_hash,name,raw_json,created_at,active)
                            VALUES($id,$v,$batch,$branch,NULL,$name,$raw,$t,1)";
        cmd.Parameters.AddWithValue("$id", s.SampleId);
        cmd.Parameters.AddWithValue("$v", version);
        cmd.Parameters.AddWithValue("$batch", batch);
        cmd.Parameters.AddWithValue("$branch", branch);
        cmd.Parameters.AddWithValue("$name", s.Name);
        cmd.Parameters.AddWithValue("$raw", s.RawJson);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
        if (s.Covariance is not null) InsertCovariance(tx, branch, batch, s.SampleId, version, s.Covariance);
    }

    private void InsertCovariance(SqliteTransaction tx, string branch, string batch, string sampleId, string sampleVersion, MeasurementCovariance cov)
    {
        string id = $"cov-{sampleId}";
        string v = Hashing.ContentId("ver", sampleVersion + MatrixToJson(cov.Matrix));
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR IGNORE INTO covariances(covariance_id,version_hash,batch_id,branch_id,sample_id,tracers_json,matrix_json,raw_token,created_at,active)
                            VALUES($id,$v,$batch,$branch,$sid,$tr,$m,$raw,$t,1)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$v", v);
        cmd.Parameters.AddWithValue("$batch", batch);
        cmd.Parameters.AddWithValue("$branch", branch);
        cmd.Parameters.AddWithValue("$sid", sampleId);
        cmd.Parameters.AddWithValue("$tr", Hashing.Canonical(cov.Tracers));
        cmd.Parameters.AddWithValue("$m", MatrixToJson(cov.Matrix));
        cmd.Parameters.AddWithValue("$raw", cov.RawToken);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
    }

    private void InsertSource(SqliteTransaction tx, string branch, string batch, SourceRecord s)
    {
        string modes = Hashing.Canonical(new
        {
            s.Tracers,
            modes = s.Modes.Select(m => new { mean = m.Mean, cov = ToJagged(m.Covariance), weight = m.Weight })
        });
        string v = Hashing.ContentId("ver", modes + s.MeansRaw + s.CovarianceRaw);
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR IGNORE INTO sources(source_id,version_hash,batch_id,branch_id,supersedes_version_hash,name,means_raw,covariance_raw,modes_json,created_at,active)
                            VALUES($id,$v,$batch,$branch,NULL,$name,$mr,$cr,$modes,$t,1)";
        cmd.Parameters.AddWithValue("$id", s.SourceId);
        cmd.Parameters.AddWithValue("$v", v);
        cmd.Parameters.AddWithValue("$batch", batch);
        cmd.Parameters.AddWithValue("$branch", branch);
        cmd.Parameters.AddWithValue("$name", s.Name);
        cmd.Parameters.AddWithValue("$mr", s.MeansRaw);
        cmd.Parameters.AddWithValue("$cr", s.CovarianceRaw);
        cmd.Parameters.AddWithValue("$modes", modes);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
    }

    public void SaveFractionation(string branchId, FractionationVersion fv, string? supersedes)
    {
        string v = Hashing.ContentId("ver", Hashing.Canonical(fv.Shifts) + fv.Basis);
        using var tx = C.BeginTransaction();
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR IGNORE INTO fractionations(fractionation_id,version_hash,branch_id,supersedes_version_hash,label,basis,shifts_json,raw_token,created_at,active)
                            VALUES($id,$v,$b,$sup,$label,$basis,$shifts,$raw,$t,1)";
        cmd.Parameters.AddWithValue("$id", fv.FractionationId);
        cmd.Parameters.AddWithValue("$v", v);
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$sup", (object?)supersedes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$label", fv.Label);
        cmd.Parameters.AddWithValue("$basis", fv.Basis);
        cmd.Parameters.AddWithValue("$shifts", Hashing.Canonical(fv.Shifts));
        cmd.Parameters.AddWithValue("$raw", fv.RawToken);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void SavePrior(string branchId, PriorAssumption prior, string? supersedes)
    {
        string v = Hashing.ContentId("ver", Hashing.Canonical(prior.Bounds) + prior.Kind);
        using var tx = C.BeginTransaction();
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT OR IGNORE INTO priors(prior_id,version_hash,branch_id,supersedes_version_hash,kind,bounds_json,raw_token,created_at,active)
                            VALUES($id,$v,$b,$sup,$kind,$bo,$raw,$t,1)";
        cmd.Parameters.AddWithValue("$id", prior.PriorId);
        cmd.Parameters.AddWithValue("$v", v);
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$sup", (object?)supersedes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", prior.Kind);
        cmd.Parameters.AddWithValue("$bo", Hashing.Canonical(prior.Bounds));
        cmd.Parameters.AddWithValue("$raw", prior.RawToken);
        cmd.Parameters.AddWithValue("$t", IsoNow());
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public static double[][] ToJagged(double[,] m)
    {
        int n = m.GetLength(0), k = m.GetLength(1);
        var rows = new double[n][];
        for (int i = 0; i < n; i++) { rows[i] = new double[k]; for (int j = 0; j < k; j++) rows[i][j] = m[i, j]; }
        return rows;
    }

    public static string MatrixToJson(double[,] m)
    {
        int n = m.GetLength(0), k = m.GetLength(1);
        var rows = new double[n][];
        for (int i = 0; i < n; i++) { rows[i] = new double[k]; for (int j = 0; j < k; j++) rows[i][j] = m[i, j]; }
        return Hashing.Canonical(rows);
    }

    public static double[,] MatrixFromJson(string json)
    {
        var rows = JsonSerializer.Deserialize<double[][]>(json, JsonOpts.Default)!;
        var m = new double[rows.Length, rows[0].Length];
        for (int i = 0; i < rows.Length; i++)
            for (int j = 0; j < rows[0].Length; j++) m[i, j] = rows[i][j];
        return m;
    }
}

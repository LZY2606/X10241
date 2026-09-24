using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using IsotopeMixing.Core.Domain;
using IsotopeMixing.Core.Identity;
using IsotopeMixing.Core.Pack;
using IsotopeMixing.Store.Schema;
using IsotopeMixing.Store.Serialization;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Store;

public sealed partial class IsotopeRepository
{
    private readonly string _connectionString;

    public IsotopeRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        SchemaInitializer.Initialize(connection);
        return connection;
    }

    public int SchemaVersion()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return SchemaInitializer.CurrentVersion(connection);
    }

    // ---- 导入：整批一个事务，任何一步失败都回滚，绝不留下半份记录 ----
    public void ImportCase(CasePack pack, IReadOnlyDictionary<string, byte[]> files, string importBatch)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();

        try
        {
            EnsureNoExistingCase(connection, tx, pack.CaseId);

            // 1) 原始字节（内容寻址）
            foreach (var (filename, bytes) in files)
            {
                string sha = StableIds.Sha256Hex(bytes);
                InsertBlob(connection, tx, sha, bytes, GuessMediaType(filename), filename);
            }

            // 2) 案例与维度
            var dimensions = pack.Dimensions
                .Select((d, i) => PackLoader.Dimension(d, i))
                .ToList();
            Execute(connection, tx,
                "INSERT INTO cases (case_id, title, description, dimensions_json, created_at, replay_log_json)" +
            "VALUES ($id, $title, $desc, $dims, $at, $replay);",
                ("$id", pack.CaseId), ("$title", pack.Title), ("$desc", pack.Description),
                ("$dims", DomainJson.Serialize(pack.Dimensions)),
                ("$at", DateTime.UtcNow.ToString("O")),
                ("$replay", DomainJson.Serialize(pack.ReplayLog)));

            // 3) 来源材料登记（每种材料指向原始字节、保留原始表示）
            RegisterMaterial(connection, tx, pack.CaseId, importBatch, "case-manifest",
                "manifest.json", "manifest.json", DomainJson.Serialize(pack));

            // 4) 样品（版本 v1）
            int ordinal = 1;
            foreach (var ps in pack.Samples)
            {
                var sample = PackLoader.Sample(ps, pack.Dimensions);
                Execute(connection, tx,
                    "INSERT INTO samples" +
            "(case_id, sample_id, label, values_json, units_json, provenance, version_id, created_at)" +
            "VALUES ($case, $id, $label, $vals, $units, $prov, $ver, $at);",
                    ("$case", pack.CaseId), ("$id", sample.Id), ("$label", sample.Label),
                    ("$vals", DomainJson.Serialize(sample.Values)),
                    ("$units", DomainJson.UnitsToJson(sample.Units).ToJsonString()),
                    ("$prov", sample.ProvenanceNote),
                    ("$ver", "v1"), ("$at", DateTime.UtcNow.ToString("O")));

                RegisterMaterial(connection, tx, pack.CaseId, importBatch, "sample",
                    ps.Id, $"samples/{ps.Id}.json", DomainJson.Serialize(ps), ordinal++);
                RegisterDiagnostics(connection, tx, pack.CaseId,
                    StableIds.Scoped("sample", pack.CaseId, ps.Id),
                    sample.Values.Where(v => v.ParseDiagnostic is not null)
                        .Select(v => v.ParseDiagnostic!)
                        .Concat(sample.Units.Where(u => u.ConversionDiagnostic is not null)
                        .Select(u => u.ConversionDiagnostic!)));
            }

            // 5) 端元集合版本
            foreach (var set in pack.EndmemberSets)
            {
                var version = PackLoader.EndmemberSet(set);
                Execute(connection, tx,
                    "INSERT INTO endmember_sets" +
            "(case_id, version_id, label, parent_version_id, endmembers_json, dimension_order_json, created_at, change_note)" +
            "VALUES ($case, $id, $label, $parent, $members, $order, $at, $note);",
                    ("$case", pack.CaseId), ("$id", version.Id), ("$label", version.Label),
                    ("$parent", version.ParentVersionId),
                    ("$members", DomainJson.EndmembersToJson(version.Endmembers).ToJsonString()),
                    ("$order", DomainJson.StringsToJson(version.DimensionOrder).ToJsonString()),
                    ("$at", DateTime.UtcNow.ToString("O")), ("$note", version.ChangeNote));

                RegisterMaterial(connection, tx, pack.CaseId, importBatch, "endmember-set",
                    set.Id, $"endmembers/{set.Id}.json", DomainJson.Serialize(set), ordinal++);
            }

            // 6) 协方差
            foreach (var cov in pack.Covariances)
            {
                var record = PackLoader.Covariance(cov);
                Execute(connection, tx,
                    "INSERT INTO covariances" +
            "(case_id, covariance_id, label, sample_id, dimension_order_json, matrix_json, is_correlated, raw_rows_json)" +
            "VALUES ($case, $id, $label, $sample, $order, $matrix, $corr, $raw);",
                    ("$case", pack.CaseId), ("$id", record.Id), ("$label", record.Label),
                    ("$sample", record.SampleId),
                    ("$order", DomainJson.StringsToJson(record.DimensionOrder).ToJsonString()),
                    ("$matrix", DomainJson.Serialize(record.Matrix)),
                    ("$corr", record.IsCorrelated ? 1L : 0L),
                    ("$raw", DomainJson.Serialize(record.RawRows)));

                RegisterMaterial(connection, tx, pack.CaseId, importBatch, "covariance",
                    cov.Id, $"covariances/{cov.Id}.json", DomainJson.Serialize(cov), ordinal++);
            }

            // 7) 分馏版本
            foreach (var frac in pack.Fractionations)
            {
                var version = PackLoader.Fractionation(frac);
                Execute(connection, tx,
                    "INSERT INTO fractionations" +
            "(case_id, version_id, label, parent_version_id, factors_json, created_at, change_note)" +
            "VALUES ($case, $id, $label, $parent, $factors, $at, $note);",
                    ("$case", pack.CaseId), ("$id", version.Id), ("$label", version.Label),
                    ("$parent", version.ParentVersionId),
                    ("$factors", DomainJson.FactorsToJson(version.Factors).ToJsonString()),
                    ("$at", DateTime.UtcNow.ToString("O")), ("$note", version.ChangeNote));

                RegisterMaterial(connection, tx, pack.CaseId, importBatch, "fractionation",
                    frac.Id, $"fractionations/{frac.Id}.json", DomainJson.Serialize(frac), ordinal++);
            }

            // 8) 先验
            foreach (var prior in pack.Priors)
            {
                var record = PackLoader.Prior(prior);
                Execute(connection, tx,
                    "INSERT INTO priors" +
            "(case_id, prior_id, label, kind, intervals_json, dirichlet_json, raw_representation)" +
            "VALUES ($case, $id, $label, $kind, $intervals, $dir, $raw);",
                    ("$case", pack.CaseId), ("$id", record.Id), ("$label", record.Label),
                    ("$kind", record.Kind),
                    ("$intervals", DomainJson.IntervalsToJson(record.Intervals).ToJsonString()),
                    ("$dir", DomainJson.DirichletToJson(record.DirichletAlphas).ToJsonString()),
                    ("$raw", record.RawRepresentation));

                RegisterMaterial(connection, tx, pack.CaseId, importBatch, "prior",
                    prior.Id, $"priors/{prior.Id}.json", DomainJson.Serialize(prior), ordinal++);
            }

            // 9) 基线分支
            foreach (var branch in pack.BaselineBranches)
            {
                CreateBranchRow(connection, tx, pack.CaseId, branch.BranchId, branch.Label,
                    branch.Parent, branch.SampleId, branch.EndmemberSet, branch.Fractionation,
                    branch.Covariance, branch.Prior);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void EnsureNoExistingCase(SqliteConnection c, SqliteTransaction tx, string caseId)
    {
        // 同一案例重复导入必须显式拒绝（避免半份覆盖）；调用方应先导出/删除。
        using var cmd = new SqliteCommand("SELECT 1 FROM cases WHERE case_id = $id;", c, tx);
        cmd.Parameters.AddWithValue("$id", caseId);
        if (cmd.ExecuteScalar() is not null)
            throw new InvalidOperationException($"案例 {caseId} 已存在，拒绝在原库上叠加导入。");
    }

    private static void InsertBlob(SqliteConnection c, SqliteTransaction tx,
        string sha, byte[] bytes, string mediaType, string filename)
    {
        using var cmd = new SqliteCommand(
            "INSERT OR IGNORE INTO raw_blobs (sha256, bytes, media_type, original_filename, imported_at)" +
            "+ VALUES ($sha, $bytes, $media, $file, $at);", c, tx);
        cmd.Parameters.AddWithValue("$sha", sha);
        cmd.Parameters.AddWithValue("$bytes", bytes);
        cmd.Parameters.AddWithValue("$media", mediaType);
        cmd.Parameters.AddWithValue("$file", filename);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void RegisterMaterial(SqliteConnection c, SqliteTransaction tx,
        string caseId, string batch, string kind, string logicalKey,
        string filename, string rawRepresentation, int ordinal = 0)
    {
        using var hashCmd = new SqliteCommand("SELECT sha256 FROM raw_blobs WHERE original_filename = $file;", c, tx);
        hashCmd.Parameters.AddWithValue("$file", filename);
        object? blobSha = hashCmd.ExecuteScalar();

        // manifest 自身按内容再落一个 blob（保证它引用的原始字节可读）。
        if (blobSha is null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(rawRepresentation);
            string sha = StableIds.Sha256Hex(bytes);
            InsertBlob(c, tx, sha, bytes, "application/json", filename);
            blobSha = sha;
        }

        string stableId = StableIds.Scoped(kind, caseId, logicalKey);
        using var cmd = new SqliteCommand(
            "INSERT INTO source_materials" +
            "+ (stable_id, case_id, kind, logical_key, blob_sha256, raw_representation, ordinal, imported_at, import_batch)" +
            "+ VALUES ($id, $case, $kind, $key, $sha, $raw, $ordinal, $at, $batch);", c, tx);
        cmd.Parameters.AddWithValue("$id", stableId);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$key", logicalKey);
        cmd.Parameters.AddWithValue("$sha", (string)blobSha);
        cmd.Parameters.AddWithValue("$raw", rawRepresentation);
        cmd.Parameters.AddWithValue("$ordinal", ordinal);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$batch", batch);
        cmd.ExecuteNonQuery();
    }

    private static void RegisterDiagnostics(SqliteConnection c, SqliteTransaction tx,
        string caseId, string materialId, IEnumerable<string> diagnostics)
    {
        int ordinal = 0;
        foreach (var diagnostic in diagnostics)
        {
            using var cmd = new SqliteCommand(
                "INSERT INTO material_diagnostics (case_id, material_id, ordinal, diagnostic)" +
            "+ VALUES ($case, $id, $ordinal, $diagnostic);", c, tx);
            cmd.Parameters.AddWithValue("$case", caseId);
            cmd.Parameters.AddWithValue("$id", materialId);
            cmd.Parameters.AddWithValue("$ordinal", ordinal++);
            cmd.Parameters.AddWithValue("$diagnostic", diagnostic);
            cmd.ExecuteNonQuery();
        }
    }

    private static string GuessMediaType(string filename) =>
        filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : "application/octet-stream";

    private static void CreateBranchRow(SqliteConnection c, SqliteTransaction tx,
        string caseId, string branchId, string label, string? parent,
        string sampleId, string endmemberSet, string fractionation, string covariance, string prior)
    {
        using var cmd = new SqliteCommand(
            "INSERT INTO branches" +
            "+ (case_id, branch_id, label, parent_branch_id, sample_id, endmember_version_id," +
            "+ fractionation_version_id, covariance_id, prior_id, head_event_id, revision_token, created_at)" +
            "+ VALUES ($case, $id, $label, $parent, $sample, $em, $frac, $cov, $prior, NULL, $token, $at);", c, tx);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", branchId);
        cmd.Parameters.AddWithValue("$label", label);
        cmd.Parameters.AddWithValue("$parent", (object?)parent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sample", sampleId);
        cmd.Parameters.AddWithValue("$em", endmemberSet);
        cmd.Parameters.AddWithValue("$frac", fractionation);
        cmd.Parameters.AddWithValue("$cov", covariance);
        cmd.Parameters.AddWithValue("$prior", prior);
        cmd.Parameters.AddWithValue("$token", StableIds.V5($"branch-created|{caseId}|{branchId}"));
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static int Execute(SqliteConnection c, SqliteTransaction tx, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = new SqliteCommand(sql, c, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    public List<string> ListCases()
    {
        using var c = Open();
        using var cmd = new SqliteCommand("SELECT case_id FROM cases ORDER BY case_id;", c);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    public CaseSummary GetCase(string caseId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT title, description, dimensions_json, replay_log_json FROM cases WHERE case_id = $id;", c);
        cmd.Parameters.AddWithValue("$id", caseId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException($"案例 {caseId} 不存在。");
        return new CaseSummary(
            caseId,
            reader.GetString(0),
            reader.GetString(1),
            DomainJson.Deserialize<List<PackDimension>>(reader.GetString(2))!,
            DomainJson.Deserialize<List<ReplayStep>>(reader.GetString(3))!);
    }

    public byte[] GetRawBlob(string caseId, string kind, string logicalKey)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT b.bytes FROM source_materials m" +
            "+ JOIN raw_blobs b ON b.sha256 = m.blob_sha256" +
            "+ WHERE m.case_id = $case AND m.kind = $kind AND m.logical_key = $key;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$key", logicalKey);
        return (byte[])cmd.ExecuteScalar()!;
    }

    public List<MaterialRow> ListMaterials(string caseId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT stable_id, kind, logical_key, blob_sha256, raw_representation, ordinal, import_batch" +
            "+ FROM source_materials WHERE case_id = $case" +
            "+ ORDER BY kind, ordinal, logical_key;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        var list = new List<MaterialRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MaterialRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetString(6)));
        }
        return list;
    }

    public List<string> GetDiagnostics(string caseId, string materialId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT diagnostic FROM material_diagnostics WHERE case_id = $case AND material_id = $id ORDER BY ordinal;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", materialId);
        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }
}

public sealed record CaseSummary(
    string CaseId,
    string Title,
    string Description,
    IReadOnlyList<PackDimension> Dimensions,
    IReadOnlyList<ReplayStep> ReplayLog);

public sealed record MaterialRow(
    string StableId,
    string Kind,
    string LogicalKey,
    string BlobSha256,
    string RawRepresentation,
    int Ordinal,
    string ImportBatch);

using System;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Store.Schema;

// 显式、顺序的状态迁移：每个迁移独立编号，迁移测试逐一验证旧版本可升到最新。
public static class SchemaInitializer
{
    public const int LatestVersion = 1;

    public static void Initialize(SqliteConnection connection)
    {
        using (var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            pragma.ExecuteNonQuery();
        int current = CurrentVersion(connection);

        if (current == 0)
        {
            Migration1(connection);
            current = 1;
        }

        if (current != LatestVersion)
            throw new InvalidOperationException($"不支持的数据库版本 {current}（最新 {LatestVersion}）。");
    }

    public static int CurrentVersion(SqliteConnection connection)
    {
        using var check = new SqliteCommand(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version';", connection);
        using var reader = check.ExecuteReader();
        if (!reader.Read()) return 0;
        reader.Close();

        using var query = new SqliteCommand("SELECT MAX(version) FROM schema_version;", connection);
        var result = query.ExecuteScalar();
        return result == DBNull.Value || result is null ? 0 : Convert.ToInt32(result);
    }

    private static void Migration1(SqliteConnection connection)
    {
        using var tx = connection.BeginTransaction();

        void Exec(string sql)
        {
            using var cmd = new SqliteCommand(sql, connection, tx);
            cmd.ExecuteNonQuery();
        }

        Exec("CREATE TABLE schema_version (" +
            "version INTEGER PRIMARY KEY," +
            "applied_at TEXT NOT NULL" +
            ");");

        // 原始字节：内容寻址；任何分支引用的原始文件永远可继续读取。
        Exec("CREATE TABLE raw_blobs (" +
            "sha256 TEXT PRIMARY KEY," +
            "bytes BLOB NOT NULL," +
            "media_type TEXT NOT NULL," +
            "original_filename TEXT NOT NULL," +
            "imported_at TEXT NOT NULL" +
            ");");

        // 来源材料（来源与解释分离）：稳定身份 + 原始表示 + 诊断。
        Exec("CREATE TABLE source_materials (" +
            "stable_id TEXT NOT NULL," +
            "case_id TEXT NOT NULL," +
            "kind TEXT NOT NULL," +
            "logical_key TEXT NOT NULL," +
            "blob_sha256 TEXT NOT NULL REFERENCES raw_blobs(sha256)," +
            "raw_representation TEXT NOT NULL," +
            "ordinal INTEGER NOT NULL," +
            "imported_at TEXT NOT NULL," +
            "import_batch TEXT NOT NULL," +
            "PRIMARY KEY (case_id, stable_id)" +
            ");");

        Exec("CREATE INDEX idx_materials_case ON source_materials(case_id, kind, ordinal);");

        Exec("CREATE TABLE material_diagnostics (" +
            "case_id TEXT NOT NULL," +
            "material_id TEXT NOT NULL," +
            "ordinal INTEGER NOT NULL," +
            "diagnostic TEXT NOT NULL," +
            "PRIMARY KEY (case_id, material_id, ordinal)" +
            ");");

        // 案例包（离线领域材料）：所有领域量携带单位/坐标基准。
        Exec("CREATE TABLE cases (" +
            "case_id TEXT PRIMARY KEY," +
            "title TEXT NOT NULL," +
            "description TEXT NOT NULL," +
            "dimensions_json TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "replay_log_json TEXT NOT NULL" +
            ");");

        // 来源版本（样品 / 端元集合 / 分馏 / 协方差 / 先验各自版本化，修订产生新版本，旧版本保留）。
        Exec("CREATE TABLE samples (" +
            "case_id TEXT NOT NULL," +
            "sample_id TEXT NOT NULL," +
            "label TEXT NOT NULL," +
            "values_json TEXT NOT NULL," +
            "units_json TEXT NOT NULL," +
            "provenance TEXT," +
            "version_id TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "PRIMARY KEY (case_id, sample_id, version_id)" +
            ");");

        Exec("CREATE TABLE endmember_sets (" +
            "case_id TEXT NOT NULL," +
            "version_id TEXT NOT NULL," +
            "label TEXT NOT NULL," +
            "parent_version_id TEXT," +
            "endmembers_json TEXT NOT NULL," +
            "dimension_order_json TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "change_note TEXT NOT NULL," +
            "PRIMARY KEY (case_id, version_id)" +
            ");");

        Exec("CREATE TABLE covariances (" +
            "case_id TEXT NOT NULL," +
            "covariance_id TEXT NOT NULL," +
            "label TEXT NOT NULL," +
            "sample_id TEXT NOT NULL," +
            "dimension_order_json TEXT NOT NULL," +
            "matrix_json TEXT NOT NULL," +
            "is_correlated INTEGER NOT NULL," +
            "raw_rows_json TEXT NOT NULL," +
            "PRIMARY KEY (case_id, covariance_id)" +
            ");");

        Exec("CREATE TABLE fractionations (" +
            "case_id TEXT NOT NULL," +
            "version_id TEXT NOT NULL," +
            "label TEXT NOT NULL," +
            "parent_version_id TEXT," +
            "factors_json TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "change_note TEXT NOT NULL," +
            "PRIMARY KEY (case_id, version_id)" +
            ");");

        Exec("CREATE TABLE priors (" +
            "case_id TEXT NOT NULL," +
            "prior_id TEXT NOT NULL," +
            "label TEXT NOT NULL," +
            "kind TEXT NOT NULL," +
            "intervals_json TEXT NOT NULL," +
            "dirichlet_json TEXT NOT NULL," +
            "raw_representation TEXT NOT NULL," +
            "PRIMARY KEY (case_id, prior_id)" +
            ");");

        // 分支：对同一份材料的重新解释产生新分支；parent 指向旧分支。
        Exec("CREATE TABLE branches (" +
            "case_id TEXT NOT NULL," +
            "branch_id TEXT NOT NULL," +
            "label TEXT NOT NULL," +
            "parent_branch_id TEXT," +
            "sample_id TEXT NOT NULL," +
            "endmember_version_id TEXT NOT NULL," +
            "fractionation_version_id TEXT NOT NULL," +
            "covariance_id TEXT NOT NULL," +
            "prior_id TEXT NOT NULL," +
            "head_event_id TEXT," +
            "revision_token TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "PRIMARY KEY (case_id, branch_id)" +
            ");");

        // 事件流（追加、不可变）：机器可读，保存被采纳与未采纳的所有结果。
        Exec("CREATE TABLE events (" +
            "case_id TEXT NOT NULL," +
            "branch_id TEXT NOT NULL," +
            "seq INTEGER NOT NULL," +
            "event_id TEXT NOT NULL," +
            "type TEXT NOT NULL," +
            "analysis_result_id TEXT," +
            "supersedes_event_id TEXT," +
            "undoes_event_id TEXT," +
            "actor TEXT NOT NULL," +
            "payload_json TEXT NOT NULL," +
            "base_revision_token TEXT," +
            "new_revision_token TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "PRIMARY KEY (case_id, branch_id, seq)" +
            ");");

        Exec("CREATE UNIQUE INDEX idx_events_id ON events(case_id, event_id);");

        // 派生分析结果（被采纳和未采纳都保存）：依赖指纹 + 规则版本，供失效范围计算。
        Exec("CREATE TABLE analysis_results (" +
            "case_id TEXT NOT NULL," +
            "result_id TEXT NOT NULL," +
            "branch_id TEXT NOT NULL," +
            "status TEXT NOT NULL," +
            "inputs_json TEXT NOT NULL," +
            "regions_json TEXT NOT NULL," +
            "candidates_json TEXT NOT NULL," +
            "residuals_json TEXT NOT NULL," +
            "findings_json TEXT NOT NULL," +
            "measured_dimensions_json TEXT NOT NULL," +
            "missing_dimensions_json TEXT NOT NULL," +
            "rule_version TEXT NOT NULL," +
            "rule_hash TEXT NOT NULL," +
            "dependency_fingerprint TEXT NOT NULL," +
            "accepted INTEGER NOT NULL DEFAULT 0," +
            "created_event_id TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "PRIMARY KEY (case_id, result_id)" +
            ");");

        // 失效登记：某个来源版本/规则变化后，按依赖关系确定的失效范围（不是全局替换）。
        Exec("CREATE TABLE invalidations (" +
            "case_id TEXT NOT NULL," +
            "invalidation_id TEXT NOT NULL," +
            "changed_kind TEXT NOT NULL," +
            "changed_version_id TEXT NOT NULL," +
            "affected_result_ids_json TEXT NOT NULL," +
            "unaffected_result_ids_json TEXT NOT NULL," +
            "reason TEXT NOT NULL," +
            "created_at TEXT NOT NULL," +
            "PRIMARY KEY (case_id, invalidation_id)" +
            ");");

        Exec($"INSERT INTO schema_version (version, applied_at)" +
            "VALUES (1, '{DateTime.UtcNow:O}');");

        tx.Commit();
    }
}

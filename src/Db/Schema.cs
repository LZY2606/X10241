using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Db;

public static class Schema
{
    public const int CurrentVersion = 3;

    public static readonly IReadOnlyList<Migration> Migrations = new[]
    {
        new Migration(1,
        @"
        CREATE TABLE schema_info(version INTEGER NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL);
        CREATE TABLE import_batches(
            batch_id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            purpose TEXT NOT NULL,
            manifest_json TEXT NOT NULL
        );
        CREATE TABLE raw_records(
            uid TEXT NOT NULL,
            batch_id TEXT NOT NULL,
            record_kind TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            raw_json TEXT NOT NULL,
            raw_text TEXT NOT NULL,
            unit TEXT,
            datum TEXT,
            byte_sha256 TEXT NOT NULL,
            parse_status TEXT NOT NULL,
            diagnostics_json TEXT NOT NULL,
            PRIMARY KEY(uid, batch_id),
            FOREIGN KEY(batch_id) REFERENCES import_batches(batch_id)
        );
        CREATE TABLE branches(
            branch_id TEXT PRIMARY KEY,
            parent_branch_id TEXT,
            name TEXT NOT NULL,
            created_at TEXT NOT NULL,
            note TEXT NOT NULL,
            baseline_batch_id TEXT
        );
        CREATE TABLE events(
            branch_id TEXT NOT NULL,
            seq INTEGER NOT NULL,
            event_id TEXT NOT NULL PRIMARY KEY,
            at TEXT NOT NULL,
            event_type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            produced_result_id TEXT,
            undo_of_event_id TEXT,
            UNIQUE(branch_id, seq),
            FOREIGN KEY(branch_id) REFERENCES branches(branch_id)
        );
        CREATE TABLE results(
            result_id TEXT PRIMARY KEY,
            branch_id TEXT NOT NULL,
            created_event_id TEXT NOT NULL,
            created_seq INTEGER NOT NULL,
            scenario_id TEXT NOT NULL,
            scenario_label TEXT NOT NULL,
            sample_uid TEXT,
            endmember_set_id TEXT NOT NULL,
            cov_uid TEXT,
            frac_version_id TEXT,
            prior_id TEXT,
            unit TEXT,
            datum TEXT,
            status TEXT NOT NULL,
            identifiability TEXT NOT NULL,
            feasible INTEGER NOT NULL,
            dof INTEGER,
            chi2 REAL,
            p_value REAL,
            best_weights_json TEXT,
            residual_json TEXT,
            region_json TEXT,
            assessments_json TEXT NOT NULL,
            inputs_json TEXT NOT NULL,
            invalidated INTEGER NOT NULL DEFAULT 0,
            invalidated_event_id TEXT,
            invalidation_scope TEXT
        );
        "),
        new Migration(2,
        @"
        CREATE TABLE decisions(
            decision_id TEXT PRIMARY KEY,
            branch_id TEXT NOT NULL,
            event_id TEXT NOT NULL,
            seq INTEGER NOT NULL,
            scenario_id TEXT NOT NULL,
            result_id TEXT,
            verdict TEXT NOT NULL,
            rationale TEXT NOT NULL,
            at TEXT NOT NULL,
            revoked INTEGER NOT NULL DEFAULT 0,
            revoke_event_id TEXT
        );
        CREATE TABLE result_dependencies(
            result_id TEXT NOT NULL,
            dependency_uid TEXT NOT NULL,
            dependency_kind TEXT NOT NULL,
            dependency_fingerprint TEXT NOT NULL,
            PRIMARY KEY(result_id, dependency_uid, dependency_kind)
        );
        "),
        new Migration(3,
        @"
        CREATE TABLE archive_runs(
            archive_id TEXT PRIMARY KEY,
            branch_id TEXT NOT NULL,
            label TEXT NOT NULL,
            at TEXT NOT NULL,
            baseline_event_id TEXT NOT NULL,
            changed_source_uid TEXT,
            change_kind TEXT,
            accepted_result_id TEXT,
            rejected_result_id TEXT
        );
        CREATE INDEX idx_events_branch_seq ON events(branch_id, seq);
        CREATE INDEX idx_results_branch ON results(branch_id);
        CREATE INDEX idx_deps_uid ON result_dependencies(dependency_uid, dependency_fingerprint);
        ")
    };

    public static int Migrate(SqliteConnection cn)
    {
        cn.Open();
        int current = 0;
        using (var check = cn.CreateCommand())
        {
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_info';";
            var existing = check.ExecuteScalar();
            if (existing is not null)
            {
                check.CommandText = "SELECT MAX(version) FROM schema_info;";
                current = Convert.ToInt32(check.ExecuteScalar());
            }
        }
        using var tx = cn.BeginTransaction();
        foreach (var m in Migrations.Where(m => m.Version > current))
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = m.Sql;
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO schema_info(version, applied_at) VALUES($v, $t);";
            cmd.Parameters.AddWithValue("$v", m.Version);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return current;
    }

    public static int GetVersion(Microsoft.Data.Sqlite.SqliteConnection cn)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_info';";
        if (cmd.ExecuteScalar() is null) return 0;
        cmd.CommandText = "SELECT MAX(version) FROM schema_info;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

public sealed record Migration(int Version, string Sql);

using Microsoft.Data.Sqlite;

namespace IsotopeApp.Storage;

/// <summary>
/// Explicit, versioned schema migrations. Each migration runs once inside a
/// transaction and is recorded in schema_migrations; re-opening an old database
/// replays exactly the migrations that produced it.
/// </summary>
public static class Migrations
{
    public sealed record Migration(int Version, string Name, string Sql);

    public static readonly IReadOnlyList<Migration> All = new[]
    {
        new Migration(1, "initial", @"
        CREATE TABLE schema_migrations (
          version INTEGER PRIMARY KEY,
          applied_at TEXT NOT NULL
        );
        CREATE TABLE raw_blobs (
          blob_id TEXT PRIMARY KEY,
          sha256 TEXT NOT NULL,
          media_type TEXT NOT NULL,
          bytes BLOB NOT NULL,
          imported_at TEXT NOT NULL
        );
        CREATE TABLE import_batches (
          batch_id TEXT PRIMARY KEY,
          raw_blob_id TEXT NOT NULL REFERENCES raw_blobs(blob_id),
          status TEXT NOT NULL,
          imported_at TEXT NOT NULL,
          diagnostics_json TEXT NOT NULL DEFAULT '[]'
        );
        CREATE TABLE samples (
          sample_id TEXT NOT NULL,
          version_hash TEXT NOT NULL,
          batch_id TEXT NOT NULL REFERENCES import_batches(batch_id),
          branch_id TEXT NOT NULL,
          supersedes_version_hash TEXT,
          name TEXT NOT NULL,
          raw_json TEXT NOT NULL,
          created_at TEXT NOT NULL,
          active INTEGER NOT NULL DEFAULT 1,
          PRIMARY KEY (sample_id, version_hash)
        );
        CREATE TABLE sources (
          source_id TEXT NOT NULL,
          version_hash TEXT NOT NULL,
          batch_id TEXT NOT NULL REFERENCES import_batches(batch_id),
          branch_id TEXT NOT NULL,
          supersedes_version_hash TEXT,
          name TEXT NOT NULL,
          means_raw TEXT NOT NULL,
          covariance_raw TEXT NOT NULL,
          modes_json TEXT NOT NULL,
          created_at TEXT NOT NULL,
          active INTEGER NOT NULL DEFAULT 1,
          PRIMARY KEY (source_id, version_hash)
        );
        CREATE TABLE covariances (
          covariance_id TEXT NOT NULL,
          version_hash TEXT NOT NULL,
          batch_id TEXT NOT NULL REFERENCES import_batches(batch_id),
          branch_id TEXT NOT NULL,
          sample_id TEXT NOT NULL,
          tracers_json TEXT NOT NULL,
          matrix_json TEXT NOT NULL,
          raw_token TEXT NOT NULL,
          created_at TEXT NOT NULL,
          active INTEGER NOT NULL DEFAULT 1,
          PRIMARY KEY (covariance_id, version_hash)
        );
        CREATE TABLE fractionations (
          fractionation_id TEXT NOT NULL,
          version_hash TEXT NOT NULL,
          branch_id TEXT NOT NULL,
          supersedes_version_hash TEXT,
          label TEXT NOT NULL,
          basis TEXT NOT NULL,
          shifts_json TEXT NOT NULL,
          raw_token TEXT NOT NULL,
          created_at TEXT NOT NULL,
          active INTEGER NOT NULL DEFAULT 1,
          PRIMARY KEY (fractionation_id, version_hash)
        );
        CREATE TABLE priors (
          prior_id TEXT NOT NULL,
          version_hash TEXT NOT NULL,
          branch_id TEXT NOT NULL,
          supersedes_version_hash TEXT,
          kind TEXT NOT NULL,
          bounds_json TEXT NOT NULL,
          raw_token TEXT NOT NULL,
          created_at TEXT NOT NULL,
          active INTEGER NOT NULL DEFAULT 1,
          PRIMARY KEY (prior_id, version_hash)
        );
        CREATE TABLE units (
          unit_id TEXT NOT NULL,
          version_hash TEXT NOT NULL,
          owner_kind TEXT NOT NULL,
          owner_id TEXT NOT NULL,
          field TEXT NOT NULL,
          code TEXT NOT NULL,
          raw_token TEXT NOT NULL,
          convertible INTEGER NOT NULL,
          PRIMARY KEY (unit_id, version_hash)
        );
        CREATE TABLE branches (
          branch_id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          parent_branch_id TEXT,
          created_from_event_id TEXT,
          created_at TEXT NOT NULL,
          archived INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE trials (
          trial_id TEXT NOT NULL,
          trial_version INTEGER NOT NULL,
          branch_id TEXT NOT NULL,
          sample_id TEXT NOT NULL,
          sample_version TEXT NOT NULL,
          fractionation_id TEXT NOT NULL,
          fractionation_version TEXT NOT NULL,
          prior_id TEXT,
          prior_version TEXT,
          source_versions_json TEXT NOT NULL,
          covariance_version TEXT,
          result_json TEXT NOT NULL,
          created_event_id TEXT NOT NULL,
          created_at TEXT NOT NULL,
          status TEXT NOT NULL,
          PRIMARY KEY (trial_id, trial_version)
        );
        CREATE TABLE events (
          event_id TEXT PRIMARY KEY,
          seq INTEGER NOT NULL,
          branch_id TEXT NOT NULL,
          parent_event_id TEXT NOT NULL,
          type TEXT NOT NULL,
          occurred_at TEXT NOT NULL,
          actor TEXT,
          payload_json TEXT NOT NULL,
          dependencies_json TEXT NOT NULL,
          invalidated_json TEXT NOT NULL,
          head_hash TEXT NOT NULL
        );
        CREATE INDEX idx_events_branch ON events(branch_id, seq);
        CREATE TABLE decisions (
          decision_id TEXT PRIMARY KEY,
          branch_id TEXT NOT NULL,
          trial_id TEXT NOT NULL,
          kind TEXT NOT NULL,
          event_id TEXT NOT NULL,
          comment TEXT,
          at TEXT NOT NULL,
          undoes_decision_id TEXT
        );
        CREATE TABLE kv_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        "),
        new Migration(2, "decision_labels", @"
        ALTER TABLE decisions ADD COLUMN label TEXT NOT NULL DEFAULT 'adjudication';
        ALTER TABLE events ADD COLUMN accepted INTEGER NOT NULL DEFAULT 1;
        ")
    };

    public static void Run(SqliteConnection db)
    {
        db.Open();
        var applied = new HashSet<int>();
        using (var q = db.CreateCommand())
        {
            q.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
            using var r = q.ExecuteReader();
            if (r.Read())
            {
                r.Close();
                using var q2 = db.CreateCommand();
                q2.CommandText = "SELECT version FROM schema_migrations";
                using var r2 = q2.ExecuteReader();
                while (r2.Read()) applied.Add(r2.GetInt32(0));
            }
        }
        foreach (var m in All)
        {
            if (applied.Contains(m.Version)) continue;
            using var tx = db.BeginTransaction();
            foreach (var stmt in m.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                using var c = db.CreateCommand();
                c.Transaction = tx;
                c.CommandText = stmt;
                c.ExecuteNonQuery();
            }
            using var ins = db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES ($v, $t)";
            ins.Parameters.AddWithValue("$v", m.Version);
            ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            ins.ExecuteNonQuery();
            tx.Commit();
        }
    }
}

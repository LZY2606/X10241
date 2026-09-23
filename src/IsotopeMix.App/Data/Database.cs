using Microsoft.Data.Sqlite;

namespace IsotopeMix.App.Data;

/// <summary>
/// SQLite schema with explicit, ordered migrations. Each migration is applied at most
/// once and recorded in schema_migrations; state migration tests exercise them stepwise.
/// </summary>
public static class Database
{
    public const int LatestVersion = 3;

    private static readonly (int Version, string Sql)[] Migrations =
    {
        (1, """
            CREATE TABLE schema_migrations(
              version INTEGER PRIMARY KEY,
              applied_at TEXT NOT NULL
            );
            CREATE TABLE import_batches(
              id TEXT PRIMARY KEY,
              created_at TEXT NOT NULL,
              status TEXT NOT NULL
            );
            CREATE TABLE source_records(
              id TEXT PRIMARY KEY,
              kind TEXT NOT NULL,
              name TEXT NOT NULL,
              version TEXT NOT NULL,
              raw_format TEXT NOT NULL,
              raw_bytes BLOB NOT NULL,
              sha256 TEXT NOT NULL,
              import_batch TEXT NOT NULL REFERENCES import_batches(id),
              seq INTEGER NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE branches(
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              parent_branch_id TEXT,
              created_from_event_id TEXT,
              head_seq INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL
            );
            CREATE TABLE events(
              id TEXT PRIMARY KEY,
              branch_id TEXT NOT NULL REFERENCES branches(id),
              seq INTEGER NOT NULL,
              type TEXT NOT NULL,
              payload TEXT NOT NULL,
              dep_versions TEXT NOT NULL,
              created_at TEXT NOT NULL,
              UNIQUE(branch_id, seq)
            );
            """),
        (2, """
            CREATE TABLE derived_results(
              id TEXT PRIMARY KEY,
              branch_id TEXT NOT NULL,
              dep_key TEXT NOT NULL,
              kind TEXT NOT NULL,
              payload TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE derived_dependencies(
              derived_id TEXT NOT NULL REFERENCES derived_results(id),
              source_ref TEXT NOT NULL,
              PRIMARY KEY(derived_id, source_ref)
            );
            """),
        (3, """
            ALTER TABLE source_records ADD COLUMN diagnostics TEXT NOT NULL DEFAULT '[]';
            ALTER TABLE derived_results ADD COLUMN superseded_by TEXT;
            """),
    };

    public static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }
        Migrate(conn);
        return conn;
    }

    public static void Migrate(SqliteConnection conn)
    {
        var current = CurrentVersion(conn);
        foreach (var (version, sql) in Migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = version == 1
                    ? "INSERT INTO schema_migrations(version, applied_at) VALUES (1, $t)"
                    : $"INSERT INTO schema_migrations(version, applied_at) VALUES ({version}, $t)";
                cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public static int CurrentVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
        if ((long)(cmd.ExecuteScalar() ?? 0L) == 0) return 0;
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations";
        return Convert.ToInt32(cmd2.ExecuteScalar());
    }
}

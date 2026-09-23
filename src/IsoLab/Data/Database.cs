using Microsoft.Data.Sqlite;

namespace IsoLab.Data;

public static class Database
{
    public const int LatestVersion = 2;

    public static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        Migrate(conn);
        return conn;
    }

    public static int CurrentVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
        if (cmd.ExecuteScalar() is null) return 0;
        using var v = conn.CreateCommand();
        v.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_migrations";
        return Convert.ToInt32(v.ExecuteScalar());
    }

    public static void Migrate(SqliteConnection conn, int targetVersion = LatestVersion)
    {
        var current = CurrentVersion(conn);
        for (var v = current + 1; v <= targetVersion; v++)
        {
            using var tx = conn.BeginTransaction();
            ApplyMigration(conn, tx, v);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES ($v, $t)";
            cmd.Parameters.AddWithValue("$v", v);
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    private static void ApplyMigration(SqliteConnection conn, SqliteTransaction tx, int version)
    {
        var sql = version switch
        {
            1 => """
                CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
                CREATE TABLE materials(
                    id TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL, version INTEGER NOT NULL,
                    supersedes TEXT NULL, raw_json TEXT NOT NULL, unit TEXT NULL, coord TEXT NULL,
                    seq INTEGER NOT NULL, import_batch TEXT NOT NULL, created_event TEXT NOT NULL,
                    PRIMARY KEY(id, version));
                CREATE TABLE events(
                    id TEXT PRIMARY KEY, branch_id TEXT NOT NULL, kind TEXT NOT NULL,
                    payload_json TEXT NOT NULL, seq INTEGER NOT NULL, created_at TEXT NOT NULL, parent_id TEXT NULL);
                CREATE TABLE branches(
                    id TEXT PRIMARY KEY, name TEXT NOT NULL, parent_branch TEXT NULL,
                    head_event TEXT NULL, version INTEGER NOT NULL, status TEXT NOT NULL);
                CREATE TABLE candidates(
                    id TEXT PRIMARY KEY, branch_id TEXT NOT NULL, source_event TEXT NOT NULL,
                    props_json TEXT NOT NULL, residual REAL NOT NULL, component INTEGER NOT NULL,
                    state TEXT NOT NULL, deps_json TEXT NOT NULL);
                CREATE TABLE decisions(
                    id TEXT PRIMARY KEY, branch_id TEXT NOT NULL, candidate_id TEXT NOT NULL,
                    action TEXT NOT NULL, note TEXT NOT NULL, event_id TEXT NOT NULL);
                """,
            // v2: history-integrity columns. Existing rows must survive the migration.
            2 => """
                ALTER TABLE candidates ADD COLUMN stale INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE decisions ADD COLUMN undone INTEGER NOT NULL DEFAULT 0;
                """,
            _ => throw new InvalidOperationException($"unknown migration {version}")
        };
        foreach (var stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = stmt;
            cmd.ExecuteNonQuery();
        }
    }
}

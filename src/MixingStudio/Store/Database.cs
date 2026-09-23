using Microsoft.Data.Sqlite;

namespace MixingStudio.Store;

/// <summary>Schema is versioned; migrations apply in order and are individually testable.</summary>
public static class Database
{
    public const int CurrentVersion = 3;

    public static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            cmd.ExecuteNonQuery();
        }
        Migrate(conn);
        return conn;
    }

    public static int SchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations'";
        if (cmd.ExecuteScalar() is null) return 0;
        using var q = conn.CreateCommand();
        q.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_migrations";
        return Convert.ToInt32(q.ExecuteScalar());
    }

    public static void Migrate(SqliteConnection conn)
    {
        int v = SchemaVersion(conn);
        if (v < 1) Apply(conn, 1, Migration1);
        if (v < 2) Apply(conn, 2, Migration2);
        if (v < 3) Apply(conn, 3, Migration3);
    }

    private static void Apply(SqliteConnection conn, int version, string sql)
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
            cmd.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES ($v, $t)";
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private const string Migration1 = @"
CREATE TABLE IF NOT EXISTS schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
CREATE TABLE materials(
    id TEXT NOT NULL,
    kind TEXT NOT NULL,
    version INTEGER NOT NULL,
    raw_json TEXT NOT NULL,
    sha256 TEXT NOT NULL,
    event_id TEXT NOT NULL,
    PRIMARY KEY(id, version)
);
CREATE TABLE branches(
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    parent_branch_id TEXT,
    base_event_id TEXT NOT NULL,
    head_seq INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE events(
    id TEXT PRIMARY KEY,
    branch_id TEXT NOT NULL,
    seq INTEGER NOT NULL,
    type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    material_refs_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE INDEX events_branch_seq ON events(branch_id, seq);
";

    // v2: derived candidates + adjudication state
    private const string Migration2 = @"
CREATE TABLE candidates(
    id TEXT PRIMARY KEY,
    event_id TEXT NOT NULL,
    branch_id TEXT NOT NULL,
    region_index INTEGER NOT NULL,
    polygon_json TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'proposed',
    adjudication_event_id TEXT
);
CREATE INDEX candidates_branch ON candidates(branch_id);
";

    // v3: dependency invalidation ledger
    private const string Migration3 = @"
CREATE TABLE invalidations(
    id TEXT PRIMARY KEY,
    event_id TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    reason TEXT NOT NULL
);
CREATE INDEX invalidations_target ON invalidations(target_type, target_id);
";
}

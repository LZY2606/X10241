using Microsoft.Data.Sqlite;

namespace StableIsotopeMixing.Web.Data;

public static class Database
{
    public const int LatestVersion = 2;

    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
            CREATE TABLE schema_migrations(
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            CREATE TABLE raw_records(
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                version INTEGER NOT NULL,
                raw_format TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                unit_basis TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                provenance_json TEXT NOT NULL,
                created_event_id TEXT
            );

            CREATE TABLE branches(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                parent_branch_id TEXT,
                base_event_id TEXT,
                head_event_id TEXT,
                status TEXT NOT NULL DEFAULT 'active',
                created_event_id TEXT
            );

            CREATE TABLE events(
                id TEXT PRIMARY KEY,
                branch_id TEXT NOT NULL REFERENCES branches(id),
                seq INTEGER NOT NULL,
                prev_event_id TEXT,
                kind TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                actor TEXT NOT NULL DEFAULT 'system',
                created_at TEXT NOT NULL,
                UNIQUE(branch_id, seq)
            );

            CREATE TABLE interpretations(
                id TEXT PRIMARY KEY,
                branch_id TEXT NOT NULL REFERENCES branches(id),
                name TEXT NOT NULL,
                config_json TEXT NOT NULL,
                created_event_id TEXT NOT NULL
            );

            CREATE TABLE analyses(
                id TEXT PRIMARY KEY,
                branch_id TEXT NOT NULL REFERENCES branches(id),
                interpretation_id TEXT NOT NULL REFERENCES interpretations(id),
                status TEXT NOT NULL,
                flags_json TEXT NOT NULL,
                diagnostics_json TEXT NOT NULL,
                deps_json TEXT NOT NULL,
                event_id TEXT NOT NULL
            );

            CREATE TABLE candidates(
                id TEXT PRIMARY KEY,
                analysis_id TEXT NOT NULL REFERENCES analyses(id),
                label TEXT NOT NULL,
                vertices_json TEXT NOT NULL,
                best_point_json TEXT NOT NULL,
                area REAL NOT NULL,
                min_mahalanobis REAL NOT NULL
            );

            CREATE TABLE invalidations(
                id TEXT PRIMARY KEY,
                event_id TEXT NOT NULL,
                branch_id TEXT NOT NULL,
                trigger_json TEXT NOT NULL,
                affected_json TEXT NOT NULL
            );
            """),
        (2, """
            CREATE TABLE conflicts(
                id TEXT PRIMARY KEY,
                branch_id TEXT NOT NULL,
                base_event_id TEXT,
                head_event_id TEXT NOT NULL,
                attempted_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX idx_events_branch_seq ON events(branch_id, seq);
            CREATE INDEX idx_analyses_branch ON analyses(branch_id);
            CREATE INDEX idx_candidates_analysis ON candidates(analysis_id);
            """)
    ];

    public static SqliteConnection Open(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        Execute(connection, null, "PRAGMA journal_mode=WAL;");
        Execute(connection, null, "PRAGMA foreign_keys=ON;");
        Migrate(connection);
        return connection;
    }

    public static void Migrate(SqliteConnection connection, int? upToVersion = null)
    {
        var target = upToVersion ?? LatestVersion;
        var applied = new HashSet<int>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT version FROM schema_migrations;";
            try
            {
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    applied.Add(reader.GetInt32(0));
                }
            }
            catch (SqliteException)
            {
                // Fresh database: schema_migrations does not exist yet.
            }
        }

        foreach (var (version, sql) in Migrations)
        {
            if (version > target || applied.Contains(version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, sql);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES ($v, $t);";
                command.Parameters.AddWithValue("$v", version);
                command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    internal static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

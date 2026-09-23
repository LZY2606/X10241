using Microsoft.Data.Sqlite;

namespace IsotopeMix.Storage;

// Ordered, append-only schema migrations. Never edit a released migration;
// add a new one instead. State migration tests exercise every step.
public static class Migrations
{
    public static readonly IReadOnlyList<(int version, string name, string sql)> Steps = new[]
    {
        (1, "baseline", """
        CREATE TABLE raw_blob (
            blob_id     TEXT PRIMARY KEY,
            file_name   TEXT NOT NULL,
            media_type  TEXT NOT NULL,
            content     BLOB NOT NULL,
            created_at  TEXT NOT NULL
        );

        CREATE TABLE import_batch (
            batch_id    TEXT PRIMARY KEY,
            label       TEXT NOT NULL,
            created_at  TEXT NOT NULL
        );

        CREATE TABLE fact_record (
            row_id        INTEGER PRIMARY KEY AUTOINCREMENT,
            stable_id     TEXT NOT NULL,
            kind          TEXT NOT NULL,
            version       INTEGER NOT NULL,
            batch_id      TEXT NOT NULL REFERENCES import_batch(batch_id),
            blob_id       TEXT NOT NULL REFERENCES raw_blob(blob_id),
            source_ref    TEXT NOT NULL,
            created_at    TEXT NOT NULL,
            payload_json  TEXT NOT NULL,
            UNIQUE(stable_id, version)
        );
        CREATE INDEX ix_fact_kind ON fact_record(kind, stable_id, version);

        CREATE TABLE branch (
            branch_id        TEXT PRIMARY KEY,
            label            TEXT NOT NULL,
            parent_event_id  TEXT NOT NULL,
            created_at       TEXT NOT NULL,
            head_event_id    TEXT NOT NULL
        );

        CREATE TABLE event_record (
            seq               INTEGER PRIMARY KEY AUTOINCREMENT,
            event_id          TEXT NOT NULL UNIQUE,
            branch_id         TEXT NOT NULL REFERENCES branch(branch_id),
            parent_event_id   TEXT NOT NULL,
            type              TEXT NOT NULL,
            actor             TEXT NOT NULL,
            created_at        TEXT NOT NULL,
            payload_json      TEXT NOT NULL
        );
        CREATE INDEX ix_event_branch ON event_record(branch_id, seq);
        CREATE INDEX ix_event_parent ON event_record(parent_event_id);

        CREATE TABLE assessment (
            assessment_id   TEXT PRIMARY KEY,
            branch_id       TEXT NOT NULL REFERENCES branch(branch_id),
            sample_id       TEXT NOT NULL,
            source_set_id   TEXT NOT NULL,
            fractionation_id TEXT NOT NULL,
            prior_id        TEXT NOT NULL,
            engine_version  TEXT NOT NULL,
            result_json     TEXT NOT NULL,
            created_event_id TEXT NOT NULL,
            UNIQUE(branch_id, assessment_id)
        );

        CREATE TABLE decision (
            decision_id     TEXT PRIMARY KEY,
            branch_id       TEXT NOT NULL REFERENCES branch(branch_id),
            event_id        TEXT NOT NULL UNIQUE,
            assessment_id   TEXT NOT NULL,
            verdict         TEXT NOT NULL
        );

        CREATE TABLE meta (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """),
        (2, "decision_note", """
        ALTER TABLE decision ADD COLUMN note TEXT NOT NULL DEFAULT '';
        """)
    };

    public static int CurrentVersion => Steps[^1].version;

    public static void Migrate(SqliteConnection conn)
    {
        int current = GetUserVersion(conn);
        foreach (var (version, _, sql) in Steps)
        {
            if (version <= current) continue;
            using var tx = conn.BeginTransaction();
            foreach (var statement in sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(statement)) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = statement;
                cmd.ExecuteNonQuery();
            }
            using (var setv = conn.CreateCommand())
            {
                setv.Transaction = tx;
                setv.CommandText = $"PRAGMA user_version={version};";
                setv.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public static int GetUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

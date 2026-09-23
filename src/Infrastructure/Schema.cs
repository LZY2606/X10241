namespace IsotopeMix.Infrastructure;

public static class Schema
{
    public const int CurrentVersion = 1;

    public static readonly IReadOnlyList<Migration> Migrations = new[]
    {
        new Migration(1, """
        CREATE TABLE schema_version(
            version INTEGER NOT NULL PRIMARY KEY,
            applied_at TEXT NOT NULL
        );

        CREATE TABLE blobs(
            sha256 TEXT NOT NULL PRIMARY KEY,
            media_type TEXT NOT NULL,
            bytes BLOB NOT NULL
        );

        CREATE TABLE raw_records(
            raw_id TEXT NOT NULL PRIMARY KEY,
            case_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            stable_id TEXT NOT NULL,
            original_json TEXT NOT NULL,
            blob_sha256 TEXT NULL REFERENCES blobs(sha256),
            source_branch_id TEXT NULL,
            created_at TEXT NOT NULL
        );
        CREATE INDEX idx_raw_case ON raw_records(case_id, kind);

        CREATE TABLE branches(
            branch_id TEXT NOT NULL PRIMARY KEY,
            case_id TEXT NOT NULL,
            name TEXT NOT NULL,
            parent_branch_id TEXT NULL,
            head_seq INTEGER NOT NULL DEFAULT 0,
            UNIQUE(case_id, name)
        );

        CREATE TABLE events(
            seq INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            event_id TEXT NOT NULL UNIQUE,
            case_id TEXT NOT NULL,
            branch_id TEXT NOT NULL,
            type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            depends_on_event_id TEXT NULL,
            basis_event_id TEXT NULL,
            author TEXT NOT NULL DEFAULT 'local',
            created_at TEXT NOT NULL
        );
        CREATE INDEX idx_events_branch ON events(branch_id, seq);
        CREATE INDEX idx_events_case ON events(case_id, seq);
        """)
    };
}

public sealed record Migration(int Version, string Sql);

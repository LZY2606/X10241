namespace IsotopeMix.Storage.Database;

/// <summary>
/// 版本化 schema 迁移。每次迁移在单个事务内执行；
/// 状态迁移测试会从干净库逐版本升级并核对结构与数据。
/// </summary>
public static class Schema
{
    public const int CurrentVersion = 3;

    public static readonly IReadOnlyList<(int Version, string Sql)> Migrations = new List<(int, string)>
    {
        (1, MigrationV1),
        (2, MigrationV2),
        (3, MigrationV3)
    };

    private const string MigrationV1 = @"
CREATE TABLE meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
CREATE TABLE import_batches (
    id TEXT PRIMARY KEY,
    note TEXT,
    rolled_back INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL
);
CREATE TABLE raw_records (
    id TEXT PRIMARY KEY,
    stable_client_id TEXT NOT NULL,
    batch_id TEXT NOT NULL REFERENCES import_batches(id),
    kind TEXT NOT NULL,
    material_key TEXT NOT NULL,
    raw_payload TEXT NOT NULL,
    raw_sha256 TEXT NOT NULL,
    original_unit_text TEXT,
    ordinal INTEGER NOT NULL,
    imported_at TEXT NOT NULL
);
CREATE INDEX ix_raw_client ON raw_records(stable_client_id);
CREATE INDEX ix_raw_material ON raw_records(material_key, kind);
CREATE TABLE branches (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    material_key TEXT NOT NULL,
    parent_branch_id TEXT,
    parent_event_ordinal INTEGER,
    head_event_id TEXT NOT NULL DEFAULT '',
    head_hash TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    is_adjudication INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE events (
    id TEXT PRIMARY KEY,
    branch_id TEXT NOT NULL REFERENCES branches(id),
    branch_ordinal INTEGER NOT NULL,
    parent_event_id TEXT NOT NULL,
    parent_hash TEXT NOT NULL,
    payload_hash TEXT NOT NULL,
    type TEXT NOT NULL,
    summary TEXT NOT NULL,
    actor TEXT NOT NULL,
    at TEXT NOT NULL,
    raw_refs_json TEXT NOT NULL,
    depends_json TEXT NOT NULL,
    invalidations_json TEXT NOT NULL,
    findings_json TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    UNIQUE(branch_id, branch_ordinal)
);
CREATE INDEX ix_events_branch ON events(branch_id, branch_ordinal);
";

    private const string MigrationV2 = @"
CREATE TABLE runs (
    id TEXT PRIMARY KEY,
    branch_id TEXT NOT NULL REFERENCES branches(id),
    created_by_event_id TEXT NOT NULL,
    result_json TEXT NOT NULL,
    endmember_version TEXT NOT NULL,
    fractionation_version TEXT NOT NULL,
    covariance_id TEXT,
    prior_id TEXT,
    rule_version TEXT NOT NULL,
    parameter_set_id TEXT NOT NULL,
    sample_id TEXT NOT NULL,
    superseded INTEGER NOT NULL DEFAULT 0,
    superseded_by_run_id TEXT,
    created_at TEXT NOT NULL
);
CREATE INDEX ix_runs_branch ON runs(branch_id);
CREATE INDEX ix_runs_deps ON runs(endmember_version, fractionation_version, rule_version, parameter_set_id);
CREATE TABLE dossiers (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    branch_id TEXT NOT NULL,
    baseline_batch_id TEXT NOT NULL,
    baseline_note TEXT NOT NULL,
    payload_json TEXT NOT NULL
);
CREATE TABLE diagnostics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    raw_record_id TEXT NOT NULL REFERENCES raw_records(id),
    field TEXT NOT NULL,
    message TEXT NOT NULL,
    raw_text TEXT NOT NULL
);
";

    private const string MigrationV3 = @"
CREATE TABLE parameter_sets (
    id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    created_at TEXT NOT NULL
);
CREATE TABLE candidate_notes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL REFERENCES runs(id),
    candidate_id TEXT NOT NULL,
    event_id TEXT NOT NULL,
    note TEXT NOT NULL
);
";
}

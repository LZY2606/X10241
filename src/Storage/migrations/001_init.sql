-- 迁移 001：事件溯源基线。raw_documents / raw_values 保存原始字节与原始表示，
-- 派生层才做规范化；所有派生结论通过 events 保持来源链。
PRAGMA journal_mode=WAL;

CREATE TABLE IF NOT EXISTS cases (
    case_id      TEXT PRIMARY KEY,
    title        TEXT NOT NULL,
    opened_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_documents (
    document_id        TEXT PRIMARY KEY,
    material_id        TEXT NOT NULL,
    material_type      TEXT NOT NULL,
    stage              TEXT NOT NULL,
    media_type         TEXT NOT NULL,
    original_filename  TEXT NOT NULL,
    content_bytes      BLOB NOT NULL,
    sha256             TEXT NOT NULL,
    imported_at        TEXT NOT NULL,
    import_ref         TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS raw_values (
    value_id         TEXT PRIMARY KEY,
    material_id      TEXT NOT NULL,
    record_key       TEXT NOT NULL,
    field            TEXT NOT NULL,
    raw_text         TEXT NOT NULL,
    unit_raw         TEXT,
    value            REAL,
    unit_canonical   TEXT,
    diagnostic       TEXT
);

CREATE TABLE IF NOT EXISTS parsed_materials (
    material_id   TEXT PRIMARY KEY,
    kind          TEXT NOT NULL,
    label         TEXT NOT NULL,
    stage         TEXT NOT NULL,
    version_of    TEXT NOT NULL,
    created_at    TEXT NOT NULL,
    diagnostics   TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS branches (
    branch_id           TEXT PRIMARY KEY,
    name                TEXT NOT NULL,
    parent_branch_id    TEXT,
    forked_from_event   TEXT,
    created_at          TEXT NOT NULL,
    created_from        TEXT NOT NULL,
    head_event_id       TEXT,
    head_seq           INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS events (
    seq               INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id          TEXT NOT NULL UNIQUE,
    branch_id         TEXT NOT NULL,
    branch_index      INTEGER NOT NULL,
    parent_event_id   TEXT,
    type              TEXT NOT NULL,
    subject_id        TEXT NOT NULL,
    payload_json      TEXT NOT NULL,
    occurred_at       TEXT NOT NULL,
    actor             TEXT NOT NULL,
    basis_rule        TEXT,
    snapshot_ref      TEXT,
    depends_on        TEXT NOT NULL DEFAULT '[]',
    UNIQUE(branch_id, branch_index)
);

CREATE TABLE IF NOT EXISTS analyses (
    analysis_id           TEXT PRIMARY KEY,
    branch_id             TEXT NOT NULL,
    at_event_seq          INTEGER NOT NULL,
    sample_id             TEXT NOT NULL,
    endmember_set_id      TEXT NOT NULL,
    fractionation_version TEXT NOT NULL,
    priors_id             TEXT NOT NULL,
    covariance_id         TEXT NOT NULL,
    result_json           TEXT NOT NULL,
    status_summary        TEXT NOT NULL,
    computed_at           TEXT NOT NULL,
    depends_material_ids  TEXT NOT NULL DEFAULT '[]',
    stale                 INTEGER NOT NULL DEFAULT 0,
    stale_reason          TEXT
);

CREATE INDEX IF NOT EXISTS idx_events_branch ON events(branch_id, branch_index);
CREATE INDEX IF NOT EXISTS idx_values_material ON raw_values(material_id);
CREATE INDEX IF NOT EXISTS idx_docs_material ON raw_documents(material_id);
CREATE INDEX IF NOT EXISTS idx_analyses_branch ON analyses(branch_id, at_event_seq);

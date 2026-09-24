-- 迁移 002：补案例元数据与人工裁定备注（迁移测试验证“旧库→002”数据保留）。
CREATE TABLE IF NOT EXISTS case_metadata (
    case_id   TEXT PRIMARY KEY REFERENCES cases(case_id),
    domain    TEXT NOT NULL DEFAULT 'stable-isotope-mixing',
    notes     TEXT NOT NULL DEFAULT ''
);

INSERT OR IGNORE INTO case_metadata(case_id, domain, notes)
SELECT case_id, 'stable-isotope-mixing', '' FROM cases;

ALTER TABLE events ADD COLUMN adjudicator_note TEXT;

using System;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

/// <summary>
/// 手写增量迁移：每个版本一个前向 DDL。状态迁移测试逐版本升级并校验。
/// 原始字节内容寻址、原始记录与解释分支分离；事件仅追加，不做就地改写。
/// </summary>
public static class Schema
{
    public const int CurrentVersion = 3;

    public static string[] Migrations { get; } = new[]
    {
        // v1: 基础对象与内容寻址字节
        @"
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE blobs (
            sha256 TEXT PRIMARY KEY,
            bytes BLOB NOT NULL,
            media_type TEXT NOT NULL,
            byte_length INTEGER NOT NULL,
            first_seen TEXT NOT NULL
        );
        CREATE TABLE raw_records (
            stable_id TEXT PRIMARY KEY,
            kind TEXT NOT NULL,
            blob_sha TEXT NOT NULL REFERENCES blobs(sha256),
            byte_offset INTEGER NOT NULL DEFAULT 0,
            byte_length INTEGER NOT NULL DEFAULT 0,
            created_event TEXT,
            created_at TEXT NOT NULL
        );
        CREATE TABLE raw_versions (
            version_id TEXT PRIMARY KEY,
            stable_id TEXT NOT NULL REFERENCES raw_records(stable_id),
            version_no INTEGER NOT NULL,
            payload_json TEXT NOT NULL,
            blob_sha TEXT NOT NULL REFERENCES blobs(sha256),
            created_event TEXT,
            created_at TEXT NOT NULL,
            UNIQUE(stable_id, version_no)
        );",
        // v2: 分支、事件日志（仅追加）、裁定
        @"
        CREATE TABLE branches (
            branch_id TEXT PRIMARY KEY,
            label TEXT NOT NULL,
            parent_branch TEXT,
            parent_event TEXT,
            basis TEXT NOT NULL,
            head_event TEXT,
            created_at TEXT NOT NULL,
            superseded INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE events (
            event_id TEXT PRIMARY KEY,
            seq INTEGER NOT NULL,
            branch_id TEXT NOT NULL REFERENCES branches(branch_id),
            at_event TEXT,
            type TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            rule_version TEXT NOT NULL,
            deps_json TEXT NOT NULL,
            source_blobs_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            superseded_by TEXT,
            superseded_scope TEXT,
            UNIQUE(branch_id, seq)
        );
        CREATE TABLE decisions (
            decision_id TEXT PRIMARY KEY,
            branch_id TEXT NOT NULL REFERENCES branches(branch_id),
            event_id TEXT NOT NULL REFERENCES events(event_id),
            kind TEXT NOT NULL,
            target_candidate TEXT,
            rationale TEXT,
            supersedes_decision TEXT,
            created_at TEXT NOT NULL
        );",
        // v3: 分析工件（不可变结果）与依赖失效索引
        @"
        CREATE TABLE artifacts (
            artifact_id TEXT PRIMARY KEY,
            event_id TEXT NOT NULL REFERENCES events(event_id),
            kind TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE invalidations (
            invalidation_id TEXT PRIMARY KEY,
            downstream_event TEXT NOT NULL REFERENCES events(event_id),
            changed_record TEXT NOT NULL,
            old_version INTEGER NOT NULL,
            new_version INTEGER NOT NULL,
            scope TEXT NOT NULL,
            created_at TEXT NOT NULL
        );"
    };

    public static int CurrentVersionValue(SqliteConnection conn)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='meta';";
        var exists = check.ExecuteScalar();
        if (exists is null) return 0;
        using var q = conn.CreateCommand();
        q.CommandText = "SELECT value FROM meta WHERE key='schema_version';";
        var v = q.ExecuteScalar();
        return v is null ? 0 : int.Parse((string)v);
    }

    public static void Initialize(SqliteConnection conn)
    {
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }
        int current = CurrentVersionValue(conn);
        for (int v = current; v < CurrentVersion; v++)
        {
            using var tx = conn.BeginTransaction();
            using var ddl = conn.CreateCommand();
            ddl.Transaction = tx;
            ddl.CommandText = Migrations[v];
            ddl.ExecuteNonQuery();
            using var upsert = conn.CreateCommand();
            upsert.Transaction = tx;
            upsert.CommandText = "INSERT INTO meta(key,value) VALUES('schema_version',@v) " +
                                 "ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            var p = upsert.CreateParameter();
            p.ParameterName = "@v"; p.Value = (v + 1).ToString();
            upsert.Parameters.Add(p);
            upsert.ExecuteNonQuery();
            tx.Commit();
        }
    }
}

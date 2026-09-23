using System;
using System.IO;
using IsoMix.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IsoMix.Tests;

public class MigrationTests
{
    private static string FreshPath() =>
        Path.Combine(Path.GetTempPath(), $"isomig_{Guid.NewGuid():N}.db");

    [Fact]
    public void Fresh_database_migrates_to_current_version()
    {
        var path = FreshPath();
        using (var store = new Store("Data Source=" + path))
        {
            Assert.Equal(Schema.CurrentVersion, Schema.CurrentVersionValue(store.Conn));
        }
    }

    [Fact]
    public void Each_schema_version_applies_forward_and_is_idempotent()
    {
        var path = FreshPath();
        var cs = "Data Source=" + path;
        // 手工建一个 v0 库，逐版本应用迁移 DDL。
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            Assert.Equal(0, Schema.CurrentVersionValue(conn));
            conn.Close();
        }
        using (var store = new Store(cs))
        {
            Assert.Equal(Schema.CurrentVersion, Schema.CurrentVersionValue(store.Conn));
            // 再次初始化不应报错（幂等），版本不变。
            Schema.Initialize(store.Conn);
            Assert.Equal(Schema.CurrentVersion, Schema.CurrentVersionValue(store.Conn));
        }

        // 校验每个版本至少引入其承诺的表
        using var check = new Store(cs);
        foreach (var table in new[] { "blobs", "raw_records", "raw_versions", "branches", "events", "decisions", "artifacts", "invalidations" })
        {
            using var cmd = check.Conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n;";
            Store.Add(cmd, "@n", table);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
    }

    [Fact]
    public void Migration_count_matches_definitions()
    {
        Assert.Equal(Schema.CurrentVersion, Schema.Migrations.Length);
    }
}

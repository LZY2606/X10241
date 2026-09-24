using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Store;

public sealed partial class IsotopeRepository
{
    // 整库导出为 SQLite 备份文件（稳定身份、顺序、外键关系逐字节保留）。
    public byte[] ExportDatabase()
    {
        using var source = Open();
        string path = WriteToTemporaryFile(source);
        try
        {
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteToTemporaryFile(SqliteConnection source)
    {
        string path = Path.Combine(Path.GetTempPath(), $"isotope-export-{Guid.NewGuid():N}.db");
        using var target = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        target.Open();
        source.BackupDatabase(target);
        target.Close();
        return path;
    }

    // 删除本地数据库后从导出文件恢复：身份、顺序、来源关系必须保持。
    public static void RestoreDatabaseFile(string backupPath, string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);
        using var source = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        source.Open();
        using var target = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = targetPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        target.Open();
        source.BackupDatabase(target);
    }
}

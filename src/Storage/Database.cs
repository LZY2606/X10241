using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;

    public string DbPath { get; }

    public Database(string dbPath)
    {
        DbPath = dbPath;
        string dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString());
        _conn.Open();
        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
    }

    public SqliteConnection Connection => _conn;

    /// <summary>按顺序应用未执行的迁移；每个迁移在独立事务内完成。</summary>
    public int Migrate()
    {
        Exec("CREATE TABLE IF NOT EXISTS schema_migrations(version TEXT PRIMARY KEY, applied_at TEXT NOT NULL);");
        var applied = new HashSet<string>();
        using (var q = _conn.CreateCommand())
        {
            q.CommandText = "SELECT version FROM schema_migrations;";
            using var r = q.ExecuteReader();
            while (r.Read()) applied.Add(r.GetString(0));
        }

        var migrations = typeof(Database).Assembly
            .GetManifestResourceNames()
            .Where(n => n.Contains("migrations.") && n.EndsWith(".sql"))
            .OrderBy(n => n)
            .ToList();

        int newly = 0;
        foreach (var res in migrations)
        {
            string version = Path.GetFileNameWithoutExtension(res.Split('.')[^2]);
            if (applied.Contains(version)) continue;
            string sql;
            using (var stream = typeof(Database).Assembly.GetManifestResourceStream(res)!)
            using (var reader = new StreamReader(stream))
                sql = reader.ReadToEnd();

            using var tx = _conn.BeginTransaction();
            foreach (string stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim()).Where(s => s.Length > 0
                                          && !s.StartsWith("--", StringComparison.Ordinal)))
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                cmd.ExecuteNonQuery();
            }
            using (var mark = _conn.CreateCommand())
            {
                mark.Transaction = tx;
                mark.CommandText = "INSERT INTO schema_migrations(version, applied_at) VALUES($v, $t);";
                mark.Parameters.AddWithValue("$v", version);
                mark.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                mark.ExecuteNonQuery();
            }
            tx.Commit();
            newly++;
        }
        return newly;
    }

    public int Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    public SqliteCommand Command(string sql, SqliteTransaction? tx = null)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        if (tx is not null) cmd.Transaction = tx;
        return cmd;
    }

    public void Dispose() => _conn.Dispose();
}

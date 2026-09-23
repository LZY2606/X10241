using Microsoft.Data.Sqlite;

namespace IsotopeMix.Storage;

public sealed class Database
{
    private readonly string _connectionString;

    public Database(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public string Path { get; }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
            pragma.ExecuteNonQuery();
        }
        return conn;
    }
}

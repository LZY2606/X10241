using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed class Database
{
    private readonly string _connectionString;
    public string Path { get; }

    public Database(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var cn = new SqliteConnection(_connectionString);
        cn.Open();
        using var pragma = cn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return cn;
    }
}

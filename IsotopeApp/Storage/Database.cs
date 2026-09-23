using Microsoft.Data.Sqlite;

namespace IsotopeApp.Storage;

/// <summary>Owns the SQLite connection and applies schema migrations.</summary>
public sealed class Database : IDisposable
{
    public SqliteConnection Connection { get; }
    public string Path { get; }

    public Database(string path)
    {
        Path = path;
        string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        Connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        Migrations.Run(Connection);
    }

    public void Dispose() => Connection.Dispose();
}

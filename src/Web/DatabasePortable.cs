using Microsoft.Data.Sqlite;
using IsotopeMixing.Db;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Web;

public static class DatabasePortable
{
    public static byte[] Export(Database db)
    {
        using var source = db.Open();
        using var backup = new SqliteConnection("Data Source=:memory:");
        backup.Open();
        source.BackupDatabase(backup);
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // Simple container: magic + raw SQLite bytes (the file itself is the portable archive).
            writer.Write(System.Text.Encoding.ASCII.GetBytes("ISOARCH1"));
        }
        using var memFile = backup.CreateCommand();
        // SqliteConnection backup to memory isn't directly readable as bytes; use sqlite backup into a temp file instead.
        ms.SetLength(0);
        var tmp = Path.Combine(Path.GetTempPath(), $"iso-export-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var fileCn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = tmp, Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString()))
            {
                fileCn.Open();
                source.BackupDatabase(fileCn);
            }
            var bytes = File.ReadAllBytes(tmp);
            using var ms2 = new MemoryStream();
            ms2.Write(System.Text.Encoding.ASCII.GetBytes("ISOARCH1"));
            ms2.Write(BitConverter.GetBytes(bytes.Length));
            ms2.Write(bytes);
            return ms2.ToArray();
        }
        finally { TryDelete(tmp); }
    }

    public static int Replace(string liveDbPath, byte[] archive)
    {
        const string magic = "ISOARCH1";
        if (archive.Length < magic.Length + 4)
            throw new InvalidDataException("bad archive: too small");
        var head = System.Text.Encoding.ASCII.GetString(archive, 0, magic.Length);
        if (head != magic) throw new InvalidDataException("bad archive: wrong magic");
        int len = BitConverter.ToInt32(archive, magic.Length);
        if (magic.Length + 4 + len != archive.Length) throw new InvalidDataException("bad archive: length mismatch");
        var sqliteBytes = archive[new Range(magic.Length + 4, magic.Length + 4 + len)];
        var tmp = liveDbPath + ".incoming";
        File.WriteAllBytes(tmp, sqliteBytes);
        // Validate it really is a sqlite database and at the expected schema before swapping.
        using (var check = new SqliteConnection($"Data Source={tmp};Mode=ReadOnly"))
        {
            check.Open();
            int v = Schema.GetVersion(check);
            if (v != Schema.CurrentVersion)
            {
                TryDelete(tmp);
                throw new InvalidDataException($"archive schema {v} != supported {Schema.CurrentVersion}");
            }
        }
        // Move with sidecar WAL/SHM cleanup.
        TryDelete(liveDbPath + "-wal");
        TryDelete(liveDbPath + "-shm");
        File.Copy(tmp, liveDbPath, overwrite: true);
        TryDelete(tmp);
        using var cn = new SqliteConnection($"Data Source={liveDbPath};Mode=ReadOnly");
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM import_batches;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void TryDelete(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); } catch { }
    }
}

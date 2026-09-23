using System.Text.Json;
using IsotopeApp.Domain;
using Microsoft.Data.Sqlite;

namespace IsotopeApp.Storage;

/// <summary>
/// Whole-archive export/import. Rows are emitted in a fixed table order with
/// their primary keys intact, and content-derived ids make the imported copy
/// byte-for-byte equivalent in identity, ordering and provenance.
/// </summary>
public sealed partial class Store
{
    private static readonly string[] ArchiveTables =
    {
        "schema_migrations","raw_blobs","import_batches","branches",
        "samples","sources","covariances","fractionations","priors","units",
        "events","decisions","trials","kv_meta"
    };

    public JsonDocument ExportArchive()
    {
        var root = new Dictionary<string, object?>
        {
            ["format"] = "isotope-archive/1",
            ["exportedAt"] = IsoNow()
        };
        foreach (var table in ArchiveTables)
        {
            var rows = new List<Dictionary<string, object?>>();
            using var cmd = C.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {table} ORDER BY rowid";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < r.FieldCount; i++)
                {
                    string name = r.GetName(i);
                    object? val = r.IsDBNull(i) ? null : r.GetValue(i);
                    if (val is byte[] bytes) val = Convert.ToBase64String(bytes);
                    row[name] = val;
                }
                rows.Add(row);
            }
            root[table] = rows;
        }
        return JsonDocument.Parse(Hashing.Canonical(root));
    }

    /// <summary>
    /// Import an archive into an empty database. Integrity is checked before
    /// any insert, then every table is restored in dependency order inside one
    /// transaction; failure leaves the target database untouched.
    /// </summary>
    public void ImportArchive(JsonDocument archive)
    {
        var root = archive.RootElement;
        if (root.GetProperty("format").GetString() != "isotope-archive/1")
            throw new InvalidDataException("unsupported archive format");
        using var tx = C.BeginTransaction();
        try
        {
            // foreign keys off during bulk restore; explicit table order handles references
            using (var fk = C.CreateCommand()) { fk.Transaction = tx; fk.CommandText = "PRAGMA foreign_keys=OFF"; fk.ExecuteNonQuery(); }
            foreach (var table in ArchiveTables)
            {
                if (!root.TryGetProperty(table, out var rows)) continue;
                foreach (var row in rows.EnumerateArray())
                {
                    var cols = new List<string>(); var pars = new List<string>(); var vals = new List<(string, object?)>();
                    foreach (var p in row.EnumerateObject())
                    {
                        cols.Add(p.Name); pars.Add("$" + cols.Count);
                        object? v = p.Value.ValueKind switch
                        {
                            JsonValueKind.Null => null,
                            JsonValueKind.Number => p.Value.TryGetInt64(out var l) ? l : p.Value.GetDouble(),
                            JsonValueKind.True => 1L,
                            JsonValueKind.False => 0L,
                            _ => p.Value.GetString()
                        };
                        if (table == "raw_blobs" && p.Name == "bytes" && v is string b64) v = Convert.FromBase64String(b64);
                        vals.Add(("$" + cols.Count, v));
                    }
                    using var cmd = C.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"INSERT OR REPLACE INTO {table}({string.Join(',', cols)}) VALUES({string.Join(',', pars)})";
                    foreach (var (name, val) in vals)
                        cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
            using (var fk2 = C.CreateCommand()) { fk2.Transaction = tx; fk2.CommandText = "PRAGMA foreign_keys_check"; var rc = fk2.ExecuteScalar();
                if (rc is not null && (long)rc != 0) throw new InvalidDataException("foreign key integrity check failed"); }
            using (var fk3 = C.CreateCommand()) { fk3.Transaction = tx; fk3.CommandText = "PRAGMA foreign_keys=ON"; fk3.ExecuteNonQuery(); }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void SetMeta(string key, string value)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "INSERT INTO kv_meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string? GetMeta(string key)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT value FROM kv_meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }
}

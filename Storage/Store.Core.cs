using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed partial class Store : IDisposable
{
    public SqliteConnection Conn { get; }
    public const string BaselineBranch = "branch.baseline";

    public Store(string connectionString)
    {
        Conn = new SqliteConnection(connectionString);
        Conn.Open();
        Schema.Initialize(Conn);
    }

    public void Dispose()
    {
        string cs = Conn.ConnectionString;
        try
        {
            using (var ck = Conn.CreateCommand()) { ck.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);"; ck.ExecuteNonQuery(); }
        }
        catch { }
        Conn.Dispose();
        // 清空连接池，避免删除数据文件后池化句柄复活旧库。
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
    }

    public static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256Text(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    private string InsertBlob(byte[] bytes, string mediaType, SqliteTransaction tx)
    {
        string sha = Sha256Hex(bytes);
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO blobs(sha256,bytes,media_type,byte_length,first_seen)
                            VALUES(@s,@b,@m,@l,@t) ON CONFLICT(sha256) DO NOTHING;";
        Add(cmd, "@s", sha); Add(cmd, "@b", bytes); Add(cmd, "@m", mediaType);
        Add(cmd, "@l", bytes.Length); Add(cmd, "@t", IsoNow());
        cmd.ExecuteNonQuery();
        return sha;
    }

    public static string IsoNow() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

    internal static void Add(SqliteCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new IsoMix.App.TwoDimDoubleConverter(), new IsoMix.App.TwoDimBoolConverter() }
    };
}

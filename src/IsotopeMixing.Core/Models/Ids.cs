using System.Security.Cryptography;
using System.Text;

namespace IsotopeMixing.Core.Models;

/// <summary>
/// 稳定身份：对同一逻辑对象的同一内容，永远生成同一字符串。
/// 记录标识用 UUIDv5（sha1 命名空间）；内容版本指纹用 sha256。
/// </summary>
public static class StableIds
{
    private const string NsRaw = "a7f3b2c1-0d4e-4f6a-9b81-3c7e5f1a2d04";

    public static Guid Namespace => Guid.Parse(NsRaw);

    /// <summary>RFC 4122 UUIDv5（name + sha1），版本 5、变体 10xx。</summary>
    public static Guid UuidV5(string name) => UuidV5(Namespace, name);

    public static Guid UuidV5(Guid ns, string name)
    {
        var nsBytes = ns.ToByteArray();
        // RFC4122 要求 big-endian 的命名空间字节
        Swap(nsBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, data, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, nsBytes.Length, nameBytes.Length);
        var hash = SHA1.HashData(data);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var result = (byte[])hash.Clone();
        Swap(result);
        return new Guid(result);
    }

    private static void Swap(byte[] b)
    {
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }

    public static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    public static string Short(string hex, int len = 12) => hex.Length <= len ? hex : hex[..len];
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace IsotopeMixing.Core.Identity;

// 稳定身份：同一逻辑材料（命名空间 + 逻辑键）在导出/删除/重新导入后仍是同一 ID。
// 使用 UUIDv5（SHA-1），命名空间固定，保证跨库、跨机器可复现。
public static class StableIds
{
    public static readonly Guid Namespace = Guid.Parse("7c1b9a3e-4d82-4f6b9-9e2d-5a8c3f1b7e06");

    public static string V5(string logicalKey)
    {
        byte[] ns = Namespace.ToByteArray();
        // RFC 4122 要求网络字节序
        Swap(ns, 0, 3);
        Swap(ns, 1, 2);
        Swap(ns, 4, 5);
        Swap(ns, 6, 7);

        byte[] name = Encoding.UTF8.GetBytes(logicalKey);
        using var sha1 = SHA1.Create();
        sha1.TransformBlock(ns, 0, ns.Length, null, 0);
        sha1.TransformFinalBlock(name, 0, name.Length);
        byte[] hash = sha1.Hash!;

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant 10
        return new Guid(hash).ToString();
    }

    public static string Scoped(string kind, string caseId, string logicalKey) =>
        V5($"{kind}|{caseId}|{logicalKey}");

    private static void Swap(byte[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);

    public static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }
}

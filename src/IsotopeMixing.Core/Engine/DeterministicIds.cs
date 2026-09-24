using System;
using System.Security.Cryptography;
using System.Text;

namespace IsotopeMixing.Core.Engine;

/// <summary>
/// 稳定身份：同一原始字节/同一语义内容永远得到同一 id。
/// 导出、删除本地库再导入后身份与来源关系保持。
/// </summary>
public static class DeterministicIds
{
    public static string Sha256Hex(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ShortHash(string content) => Sha256Hex(content)[..12];
}

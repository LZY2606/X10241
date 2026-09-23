using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsotopeMix.Core.Models;

/// <summary>
/// 稳定身份：原始记录用客户端 id + 内容哈希；事件用分支序号+哈希链。
/// 导出→删库→导入后身份、顺序与来源关系保持一致。
/// </summary>
public static class StableIds
{
    public static string Sha256Hex(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string RawRecordId(string batchId, string clientId, string rawPayload)
        => $"raw:{clientId}:{Sha256Hex(rawPayload).Substring(0, 16)}";

    public static string EventId(string branchId, long ordinal, string payloadHash)
        => $"evt:{branchId}:{ordinal}:{payloadHash.Substring(0, 12)}";

    public static string RunId(string sampleId, string emVersion, string fracVersion, string rule, string paramSet)
        => $"run:{sampleId}:{emVersion}:{fracVersion}:{rule}:{paramSet}";

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

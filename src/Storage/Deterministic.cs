using System.Security.Cryptography;
using System.Text;

namespace IsotopeMixing.Storage;

/// <summary>
/// 稳定身份：同一业务键在任何重放、导出再导入后得到同一 ID，
/// 从而保证“每个显示值带稳定身份”以及顺序与来源关系可重建。
/// </summary>
public static class Deterministic
{
    public static string Hash(string prefix, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return prefix + "_" + Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    public static string MaterialId(string caseId, string kind, string stage, string versionOf, string contentSha) =>
        Hash("mat", $"material|{caseId}|{kind}|{stage}|{versionOf}|{contentSha}");

    public static string DocumentId(string materialId, string filename, string sha) =>
        Hash("doc", $"document|{materialId}|{filename}|{sha}");

    public static string ValueId(string materialId, string recordKey, string field) =>
        Hash("val", $"value|{materialId}|{recordKey}|{field}");

    public static string EventId(string branchId, long branchIndex, string type, string subject, string payload) =>
        Hash("evt", $"event|{branchId}|{branchIndex}|{type}|{subject}|{payload}");

    public static string AnalysisId(string branchId, long atSeq, string sampleId) =>
        Hash("anl", $"analysis|{branchId}|{atSeq}|{sampleId}");

    public static string BranchId(string caseId, string name) =>
        Hash("br", $"branch|{caseId}|{name}");

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

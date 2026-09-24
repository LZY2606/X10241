using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

// ---- 原始层：来源（source）与解释（interpretation）分离保存 ----

public sealed class RawSample
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Material { get; set; } = "";
    public string Site { get; set; } = "";
    /// <summary>原始字符串到规范化量的逐维记录；缺失/不可转换也保留。</summary>
    public Dictionary<string, RawQuantity> Deltas { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class RawEndmember
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, RawQuantity> Means { get; set; } = new();
    /// <summary>各同位素维的端元标准差（δ 单位，permille）。</summary>
    public Dictionary<string, double> Sd { get; set; } = new();
    public string? ModeId { get; set; }
    public string? ModeLabel { get; set; }
}

public sealed class RawEndmemberSet
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<RawEndmember> Members { get; set; } = new();
}

public sealed class RawCovariance
{
    public string Id { get; set; } = "";
    public string SampleId { get; set; } = "";
    /// <summary>逐维方差（permille^2）；对角线之外即相关误差。</summary>
    public double[][] Matrix { get; set; } = Array.Empty<double[]>();
    public List<string> Dimensions { get; set; } = new();
    public string Record { get; set; } = ""; // 原始记录方式，例如“实验室表 C-7, ρ13,18=0.62”
    public bool HasCorrelation { get; set; }
}

public sealed class RawFractionation
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>分馏校正 δ校正 = δ实测 - shift（permille，逐维）。</summary>
    public Dictionary<string, double> Shift { get; set; } = new();
    public string Basis { get; set; } = "";
}

public sealed class RawPrior
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>对端元成员代码施加的可行比例区间 [lo,hi]；空表示无额外先验。</summary>
    public Dictionary<string, double[]> Bounds { get; set; } = new();
    public string Basis { get; set; } = "";
}

public sealed class Diagnostic
{
    public string Severity { get; set; } = "error"; // info | warning | error
    public string Code { get; set; } = "";
    public string Where { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>一次导入批次的完整原始档案。失败时整批不写入数据库。</summary>
public sealed class RawBatch
{
    public string BatchId { get; set; } = "";
    public string Label { get; set; } = "";
    public string ImportedAt { get; set; } = "";
    public string Origin { get; set; } = "";
    public string RawPayloadSha256 { get; set; } = "";
    public string RawPayloadBlobId { get; set; } = "";
    public List<RawSample> Samples { get; set; } = new();
    public List<RawEndmemberSet> EndmemberSets { get; set; } = new();
    public List<RawCovariance> Covariances { get; set; } = new();
    public List<RawFractionation> Fractionations { get; set; } = new();
    public List<RawPrior> Priors { get; set; } = new();
    public List<Diagnostic> Diagnostics { get; set; } = new();
}

// ---- 稳定身份：所有派生身份都由其内容与引用决定，导出/导入保持一致 ----

public static class StableIds
{
    public static Guid UuidV5(Guid namespaceId, string name)
    {
        Span<byte> ns = stackalloc byte[16];
        namespaceId.TryWriteBytes(ns, bigEndian: true, out _);
        Span<byte> input = stackalloc byte[16 + System.Text.Encoding.UTF8.GetMaxByteCount(name.Length)];
        ns.CopyTo(input);
        var len = System.Text.Encoding.UTF8.GetBytes(name, input[16..]);
        var hash = System.Security.Cryptography.SHA1.HashData(input[..(16 + len)]);
        var g = hash.AsSpan(0, 16).ToArray();
        g[6] = (byte)((g[6] & 0x0F) | 0x50);
        g[8] = (byte)((g[8] & 0x3F) | 0x80);
        return new Guid(g, true);
    }

    public static readonly Guid Namespace = Guid.Parse("6f4f2a7c-9b1e-5d3a-8c24-1a2b3c4d5e6f");

    public static string Deterministic(params object[] parts)
        => UuidV5(Namespace, string.Join("|", parts)).ToString();
}

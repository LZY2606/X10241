using System.Text.Json.Serialization;

namespace IsotopeMix.Core.Models;

/// <summary>来源层的记录种类。来源层永远不被原地改写。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecordKind
{
    Sample,
    Endmember,
    Covariance,
    Fractionation,
    Prior,
    UnitDeclaration
}

/// <summary>
/// 来源记录：原始字节 + 稳定身份。
/// RawPayload 是导入时逐字保留的 JSON 片段，任何派生解释都引用它，不覆盖它。
/// </summary>
public sealed class RawRecord
{
    public string Id { get; set; } = "";
    public string StableClientId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public RecordKind Kind { get; set; }
    public string MaterialKey { get; set; } = "";
    public string RawPayload { get; set; } = "";   // 原始 JSON 文本
    public string RawSha256 { get; set; } = "";
    public string? OriginalUnitText { get; set; }
    public int Ordinal { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}

/// <summary>带单位的测量值：数值、不确定度与原始表示分离。</summary>
public sealed class MeasuredValue
{
    public double RawValue { get; set; }
    public double? RawSigma { get; set; }
    public string RawText { get; set; } = "";
    public double? CanonicalPermil { get; set; }
    public double? CanonicalSigmaPermil { get; set; }
    public string? Diagnostic { get; set; }
}

public sealed class SampleRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string MaterialKey { get; set; } = "";
    public Dictionary<string, MeasuredValue> Isotopes { get; set; } = new();
    public string? CovarianceRef { get; set; }
    public string? PriorRef { get; set; }
}

public sealed class EndmemberMember
{
    public string MemberName { get; set; } = "";
    public Dictionary<string, MeasuredValue> Isotopes { get; set; } = new();
}

/// <summary>
/// 端元“分布”：支持单点与含方差的分布（均值/标准差）。
/// VersionLabel 用于同一份材料的重新解释分支（v1/v2…）。
/// </summary>
public sealed class EndmemberDistribution
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string MaterialKey { get; set; } = "";
    public string VersionLabel { get; set; } = "v1";
    public bool Distributional { get; set; }
    public List<EndmemberMember> Members { get; set; } = new();
}

/// <summary>测量协方差（样品不确定度，可含相关项）。</summary>
public sealed class CovarianceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Dimensions { get; set; } = new();
    public double[,] Matrix { get; set; } = Array2D(0);
    public static double[,] Array2D(int n) => new double[Math.Max(n, 0), Math.Max(n, 0)];
}

/// <summary>
/// 分馏版本：同位素维 → 分馏量（‰，源→混合）。
/// 切换版本产生新的规则版本引用，不覆盖旧版本。
/// </summary>
public sealed class FractionationRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VersionLabel { get; set; } = "v1";
    public Dictionary<string, double> OffsetsPermil { get; set; } = new();
}

/// <summary>先验（盒形约束；不可行先验即任何比例都无法满足）。</summary>
public sealed class PriorRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, double> LowerFraction { get; set; } = new();
    public Dictionary<string, double> UpperFraction { get; set; } = new();
}

public sealed class UnitDeclaration
{
    public string Id { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string KindText { get; set; } = "permil";
    public string Reference { get; set; } = "";
    public double? Scale { get; set; }
}

using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Domain;

/// <summary>
/// 原始记录层：导入什么就存什么。派生分析才允许规范化；无法转换的值留在原位置并附诊断。
/// </summary>
public sealed record RawRepresentation
{
    public string Text { get; init; } = "";
    public string? Unit { get; init; }
    public string? Scale { get; init; }
    public string? Note { get; init; }
    public string? ConvertDiagnostic { get; set; }

    [JsonIgnore]
    public bool HasValue => !string.IsNullOrWhiteSpace(Text);
}

public sealed record TracerDef
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public string Unit { get; init; } = "per_mil_VPDB";
    public string? ReferenceScale { get; init; }
}

public sealed record Measurement
{
    public required string TracerCode { get; init; }
    public required RawRepresentation Value { get; init; }
    public double? Numeric { get; init; }
}

/// <summary>按 tracerCode 索引的方差/协方差块；缺项表示该维没有误差估计。</summary>
public sealed record CovarianceMatrix
{
    public required string[] TracerCodes { get; init; }
    public required double[,] Values { get; init; }
    public string Unit { get; init; } = "per_mil_squared";
    public string? Note { get; init; }
}

public sealed record EndmemberPoint
{
    public required string TracerCode { get; init; }
    public required RawRepresentation Value { get; init; }
    public double? Numeric { get; init; }
}

public sealed record Endmember
{
    public required string EndmemberId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<EndmemberPoint> Points { get; init; }
    public CovarianceMatrix? Covariance { get; init; }
    public string? Note { get; init; }
}

public sealed record Sample
{
    public required string SampleId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<Measurement> Measurements { get; init; }
    public CovarianceMatrix? Covariance { get; init; }
    public string? Note { get; init; }
}

/// <summary>分馏表版本：按端元 id 给出各 tracer 的分馏均值（单位与样品一致）。</summary>
public sealed record FractionationTable
{
    public required string FractionationVersionId { get; init; }
    public string Label { get; init; } = "";
    public required string TracerCode { get; init; }
    public required IReadOnlyDictionary<string, double> ShiftsByEndmember { get; init; }
    public string? Note { get; init; }
}

/// <summary>对某端元比例的先验（不做截断归一化；不可行就显式报出）。</summary>
public sealed record PriorConstraint
{
    public required string EndmemberId { get; init; }
    public required string Kind { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 1.0;
    public double? Mean { get; init; }
    public double? StdDev { get; init; }
    public string? Note { get; init; }
}

/// <summary>一个“分馏假设”：引用一组分馏版本（每个观测 tracer 一个）。</summary>
public sealed record FractionationHypothesis
{
    public required string HypothesisId { get; init; }
    public string Label { get; init; } = "";
    public required IReadOnlyDictionary<string, string> TracerToVersion { get; init; }
}

/// <summary>一次可行域分析请求：固定样品、参与端元集合与若干并列分馏假设。</summary>
public sealed record AnalysisRequest
{
    public required string SampleId { get; init; }
    public required IReadOnlyList<string> EndmemberIds { get; init; }
    public required IReadOnlyList<FractionationHypothesis> Hypotheses { get; init; }
    public IReadOnlyList<PriorConstraint>? Priors { get; init; }
    public double ConfidenceLevel { get; init; } = 0.95;
}

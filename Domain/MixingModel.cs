using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

public sealed record IsotopeAxis(string Key, string Label, string Unit, string Datum);

public sealed record SourcePoint(
    string SourceId, string SourceName,
    IReadOnlyList<Quantity> Values,
    IReadOnlyList<IReadOnlyList<double?>>? Covariance = null);

public sealed record SamplePoint(
    string SampleId, string SampleName,
    IReadOnlyList<Quantity> Values,
    IReadOnlyList<IReadOnlyList<double?>> Covariance);

public sealed record FractionationVersion(
    string VersionId, string Label,
    IReadOnlyDictionary<string, IReadOnlyList<double?>> ShiftsBySource);

/// <summary>
/// 先验假设。Regions 可为多个不相交区域（多可行区检验）；
/// 不可行先验（空区域或空集）通过 Empty=true 显式声明。
/// </summary>
public sealed record PriorVersion(
    string VersionId, string Label, bool Empty,
    IReadOnlyList<PriorRegion> Regions);

public sealed record PriorRegion(
    IReadOnlyDictionary<string, (double lo, double hi)> Bounds);

/// <summary>一次混合求解的规范化输入（派生层，引用的全部是保留原始记录的 Quantity）。</summary>
public sealed record AnalysisInput
{
    public required string CaseId { get; init; }
    public required string SampleId { get; init; }
    public required IReadOnlyList<IsotopeAxis> Axes { get; init; }
    public required IReadOnlyList<SourcePoint> Sources { get; init; }
    public required SamplePoint Sample { get; init; }
    public required FractionationVersion Fractionation { get; init; }
    public required PriorVersion Prior { get; init; }
    public required double ConfidenceLevel { get; init; }
    /// <summary>实际参与计算的维度：无法转换/缺失的同位素维留在原始记录里，不进入矩阵。</summary>
    public required IReadOnlyList<int> ActiveDimensions { get; init; }
}

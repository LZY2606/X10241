using System.Text.Json.Serialization;

namespace IsotopeMix.Core;

/// <summary>所有领域量必须携带单位或坐标基准。value 为 null 表示无法规范化的原始值。</summary>
public sealed record QuantValue(
    double? Value,
    string Unit,
    string? Datum,
    string Raw,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Diagnostic = null)
{
    public bool IsConvertible => Value.HasValue && Diagnostic is null;
}

/// <summary>分馏应用方式：delta 空间加性修正、R 空间乘性修正。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FractionationMode
{
    DeltaAdditive,
    RatioMultiplicative
}

/// <summary>一个同位素维（如 δ15N vs Air）的定义。</summary>
public sealed record IsotopeAxis(
    string Key,
    string Label,
    string Unit,
    string Datum);

/// <summary>
/// 原始记录基类。原始层只保存导入字节的解释结果，不做不可逆清洗：
/// 无法转换的值留在原位置（Raw + Diagnostic），不删除、不为画图而丢值。
/// </summary>
public abstract record RawRecord(string Kind, string LogicalKey, string RecordVersion, string? SourceRef)
{
    public string RecordId { get; init; } = string.Empty;
    public string RawJson { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
}

public sealed record SampleRecord(
    string LogicalKey,
    string RecordVersion,
    string? SourceRef,
    string Name,
    IReadOnlyList<QuantValue> Measurements)
    : RawRecord("sample", LogicalKey, RecordVersion, SourceRef);

public sealed record EndmemberRecord(
    string LogicalKey,
    string RecordVersion,
    string? SourceRef,
    string Name,
    IReadOnlyList<QuantValue> Means,
    /// <summary>端元自身组成不确定度（可与测量相关误差不同）。</summary>
    IReadOnlyList<IReadOnlyList<double?>>? EndmemberCovariance)
    : RawRecord("endmember", LogicalKey, RecordVersion, SourceRef);

/// <summary>测量协方差（允许相关误差），按 axis_keys 给出完整对称矩阵。</summary>
public sealed record MeasurementCovarianceRecord(
    string LogicalKey,
    string RecordVersion,
    string? SourceRef,
    IReadOnlyList<string> AxisKeys,
    IReadOnlyList<IReadOnlyList<double?>> Matrix)
    : RawRecord("measurement_covariance", LogicalKey, RecordVersion, SourceRef);

/// <summary>分馏版本。同一逻辑键的新版本会让依赖它的旧分析失效（局部失效，非全局替换）。</summary>
public sealed record FractionationRecord(
    string LogicalKey,
    string RecordVersion,
    string? SourceRef,
    string Name,
    FractionationMode Mode,
    /// <summary>每个同位素维的分馏量；单位必须与轴一致。键为 axis key。</summary>
    IReadOnlyDictionary<string, QuantValue> Shifts)
    : RawRecord("fractionation", LogicalKey, RecordVersion, SourceRef);

public enum PriorKind
{
    /// <summary>仅单纯形：p_k ≥ 0，∑p_k = 1。</summary>
    Simplex,
    /// <summary>对单个比例给闭区间 [lo, hi]。</summary>
    ComponentBox,
    /// <summary>整个单纯形都被排除的先验（反例用）。</summary>
    Impossible
}

public sealed record PriorConstraint(
    string? EndmemberKey,
    double? Lo,
    double? Hi);

public sealed record PriorRecord(
    string LogicalKey,
    string RecordVersion,
    string? SourceRef,
    string Name,
    PriorKind Kind,
    IReadOnlyList<PriorConstraint> Constraints)
    : RawRecord("prior", LogicalKey, RecordVersion, SourceRef);

/// <summary>单位表：供原始表示到规范化层的转换（派生层才规范化）。</summary>
public sealed record UnitConventionRecord(
    string LogicalKey,
    string RecordVersion,
    string? SourceRef,
    IReadOnlyList<IsotopeAxis> Axes,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ConversionFactors,
    IReadOnlyDictionary<string, string> DatumAliases)
    : RawRecord("unit_convention", LogicalKey, RecordVersion, SourceRef);

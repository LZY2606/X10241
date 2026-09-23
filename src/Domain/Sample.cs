namespace IsoMix.Domain;

/// <summary>样品（解释层）。引用原始记录字节，不内联清洗后的数字作为唯一真相。</summary>
public sealed record Sample(
    string SampleId,
    string Label,
    string Material,
    IReadOnlyList<Measurement> Measurements,
    string RecordId,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);

/// <summary>端元：支持单峰/多峰分布（多峰用于"多个可行区"情形）。</summary>
public sealed record EndMember(
    string EndMemberId,
    string Label,
    string VersionId,
    IReadOnlyList<EndMemberMode> Modes,
    IReadOnlyList<string> Dimensions,
    string RecordId);

/// <summary>端元分布的一个成分：均值向量 + 协方差（元素顺序对齐 Dimensions）。</summary>
public sealed record EndMemberMode(
    string ModeId,
    IReadOnlyList<double> Mean,
    double[][] Covariance,
    double Weight);

/// <summary>分馏版本：端元到混合物有效位置的偏移。显式记录符号约定。</summary>
public sealed record FractionationVersion(
    string FractionationId,
    string Label,
    string SignConvention,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ShiftByEndMember,
    string RecordId,
    bool Retired,
    string? SupersededByFractionationId);

/// <summary>测量协方差（样品维，允许相关误差）。元素顺序对齐。</summary>
public sealed record MeasurementCovariance(
    string CovarianceId,
    string SampleId,
    IReadOnlyList<string> Dimensions,
    double[][] Matrix,
    string RecordId);

/// <summary>先验假设：对比例的约束；可与可行域不相交（不可行先验）。</summary>
public sealed record PriorAssumption(
    string PriorId,
    string SampleId,
    string Label,
    IReadOnlyDictionary<string, double> MinFraction,
    IReadOnlyDictionary<string, double> MaxFraction,
    string RecordId);

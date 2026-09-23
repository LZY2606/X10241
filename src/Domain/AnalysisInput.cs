using System.Collections.Generic;

namespace IsoMix.Domain;

/// <summary>一个端元在某个分馏版本下的有效位置（派生层，绝不替换原始端元记录）。</summary>
public sealed record EffectiveSource(
    string EndMemberId,
    string ModeId,
    string ModeLabel,
    double[] Mean,
    double[][] Covariance,
    double ModeWeight);

/// <summary>
/// 某次混合分析的规范化输入。Dimensions 为样品与端元共有的非缺失维；
/// 缺失同位素维不投影为零，而是从分析维中排除并记诊断。
/// </summary>
public sealed record AnalysisInput(
    string CaseId,
    string SampleId,
    string FractionationId,
    string EndMemberVersionId,
    string PriorId,
    IReadOnlyList<string> Dimensions,
    double[] SampleValue,
    IReadOnlyList<EffectiveSource> Sources,
    double[][] MeasurementCovariance,
    double[] PriorLower,
    double[] PriorUpper,
    bool PriorActive,
    IReadOnlyList<string> MissingDimensions,
    IReadOnlyList<ConversionDiagnostic> ConversionDiagnostics,
    IReadOnlyDictionary<string, string> SourceRecordIds);

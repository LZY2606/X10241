using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Engine;

/// <summary>
/// 派生层输入: 由原始记录按指定版本装配。规范化只发生在这里,
/// 原始 RawRecord.Content 永不被改写; 无法转换的值留在原位置并附诊断。
/// </summary>
public sealed class AnalysisInput
{
    public required string CaseId { get; init; }
    public required RawRecord SampleRecord { get; init; }
    public required List<RawRecord> EndmemberRecords { get; init; }
    public required List<RawRecord> CovarianceRecords { get; init; }
    public required RawRecord FractionationRecord { get; init; }
    public RawRecord? PriorRecord { get; init; }
    public required List<RawRecord> DimensionRecords { get; init; }
    public required Provenance Provenance { get; init; }
}

public sealed class PreparedSample
{
    public required SampleContent Content { get; init; }
    public required List<string> Dimensions { get; init; }       // 可参与计算的维
    public required List<string> Missing { get; init; }
    public required List<string> Unconvertible { get; init; }
    public required double[] Vector { get; init; }               // 已做分馏校正
    public required double[][] Covariance { get; init; }         // 样品测量协方差
    public required Dictionary<string, string> Units { get; init; }
}

public sealed class PreparedEndmember
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required RawRecord Record { get; init; }
    public required List<int> ModeIndices { get; init; }
    public required double[][] ModeMeans { get; init; }          // modes x dims (含分馏校正)
    public required double[][]? Covariance { get; init; }
    public required List<string> ModeIds { get; init; }
}

public sealed class PreparedPrior
{
    public required double[] Lower { get; init; }
    public required double[] Upper { get; init; }
    public required bool SelfConsistent { get; init; }
}

public sealed class PreparedInput
{
    public required AnalysisInput Source { get; init; }
    public required List<string> Dimensions { get; init; }
    public required Dictionary<string, string> DimensionLabels { get; init; }
    public required PreparedSample Sample { get; init; }
    public required List<PreparedEndmember> Endmembers { get; init; }
    public required double[] FractionationOffset { get; init; }
    public required FractionationContent Fractionation { get; init; }
    public PreparedPrior? Prior { get; init; }
    public required List<Diagnostic> Diagnostics { get; init; }
}

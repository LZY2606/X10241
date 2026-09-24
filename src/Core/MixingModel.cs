namespace IsotopeMixing.Core;

public enum MixingStatus
{
    Unique,
    MultipleCandidates,
    UnderIdentified,
    Infeasible,
    OutsideEnvelope,
    MissingDimensions,
    InfeasiblePrior
}

/// <summary>单个端元在各同位素维上的均值与单位。</summary>
public sealed record SourceInput(
    string SourceId,
    string Label,
    double?[] Mean,
    string[] Units,
    bool[] Present);

/// <summary>混合模型一次求解的完整输入（已在派生层规范化）。</summary>
public sealed record MixingRequest(
    string SampleId,
    string? SampleLabel,
    double?[] SampleMean,
    bool[] SamplePresent,
    double[][] SampleCovariance,
    IReadOnlyList<SourceInput> Sources,
    double[] Fractionation,
    string FractionationVersion,
    double?[] PriorLower,
    double?[] PriorUpper,
    string UnitBasis,
    double ConfidenceLevel = 0.95)
{
    public int Dimension => SampleMean.Length;
}

public sealed record FeasibleVertex(
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<double> Fractions,
    double Chi2,
    double PValue,
    bool Accepted,
    string? RejectReason);

public sealed record CandidateMix(
    string CandidateId,
    string SampleId,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<double> Fractions,
    double[][] FractionCovariance,
    double[] Residual,
    double Chi2,
    double PValue,
    int DegreesOfFreedom,
    bool WithinEnvelope,
    bool PriorSatisfied,
    bool Accepted,
    IReadOnlyList<string> Flags);

public sealed record MixingResult(
    string SampleId,
    string? SampleLabel,
    MixingStatus Status,
    string FractionationVersion,
    int UsedDimensions,
    int PresentDimensions,
    double? MinSingularRatio,
    bool RankDeficient,
    bool NearCollinear,
    bool OutsideEnvelope,
    bool PriorInfeasible,
    double EnvelopeMahalanobis2,
    IReadOnlyList<CandidateMix> Candidates,
    IReadOnlyList<FeasibleVertex> Vertices,
    IReadOnlyList<string> UsedDimensionLabels,
    IReadOnlyList<string> MissingDimensionLabels,
    IReadOnlyList<MachineJudgment> Judgments);

/// <summary>机器可读取的判断事件依据（规则、状态、对象、阈值、值）。</summary>
public sealed record MachineJudgment(
    string Rule,
    string Severity,
    bool Holds,
    string Subject,
    string Detail,
    IReadOnlyDictionary<string, object?> Evidence);

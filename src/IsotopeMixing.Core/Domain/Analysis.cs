using System.Collections.Generic;

namespace IsotopeMixing.Core.Domain;

public enum FindingCode
{
    NearCollinearEndmembers,
    SampleOutsideEnvelope,
    CorrelatedErrors,
    MissingIsotopeDimension,
    FractionationVersionSwitch,
    MultipleFeasibleRegions,
    InfeasiblePrior,
    SingleFeasibleRegion,
    EmptyFeasibleSet,
    IdentifiablePoint
}

public enum AnalysisStatus
{
    Unique,                 // 唯一可识别（点）
    MultiCandidate,         // 多候选 / 区间，保留全部合法候选
    Underidentified,        // 端元近共线：明确报可识别性不足
    DimensionLimited,       // 缺失同位素维：无法唯一决定
    Infeasible,             // 样品超出包络 / 先验不可行：无合法解
    InvalidInput
}

public sealed record Finding(
    FindingCode Code,
    string Message,
    IReadOnlyDictionary<string, object?> Evidence);

// 残差按“被测维度”报告，并附相关误差传播后的 Mahalanobis 距离（不是逐维独立误差）。
public sealed record ResidualReport(
    IReadOnlyList<string> DimensionOrder,
    IReadOnlyList<double> Vector,
    double MahalanobisSquared,
    double ChiSquareThreshold,
    bool WithinTolerance);

// 一个候选混合比例：直接给出满足单纯形约束的权重；任何情况下都不做截断后归一化。
public sealed record MixingCandidate(
    IReadOnlyList<string> EndmemberIds,
    IReadOnlyList<double> Weights,
    double MahalanobisSquared,
    IReadOnlyList<int> RegionIndices);

// 一个连通的可行区域（用网格上的权重点表示，供 SVG 画三角形/四面体可行域）。
public sealed record FeasibleRegion(
    int Index,
    string Label,
    IReadOnlyList<double[]> WeightPoints,
    IReadOnlyList<MixingCandidate> Representatives,
    int ModeComboKey);

public sealed record AnalysisResult(
    AnalysisInputs Inputs,
    AnalysisStatus Status,
    IReadOnlyList<FeasibleRegion> Regions,
    IReadOnlyList<MixingCandidate> AllCandidates,
    IReadOnlyList<ResidualReport> Residuals,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> MeasuredDimensions,
    IReadOnlyList<string> MissingDimensions,
    string RuleVersion,
    string RuleHash);

// 机器可读事件。审阅者的接受/否定/参数调整、以及系统判定，都追加为不可变事件。
public sealed record DecisionEvent(
    long Seq,
    string EventId,
    string BranchId,
    string Type,
    string? AnalysisResultId,
    string? SupersedesEventId,
    string? UndoesEventId,
    string Actor,
    string PayloadJson,
    string? BaseRevisionToken,
    string NewRevisionToken,
    System.DateTimeOffset CreatedAt);

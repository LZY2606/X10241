using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

// ==================== 原始记录（来源层：导入即原样保存，禁止不可逆清洗） ====================

public sealed record DimensionDef(string Key, string Label, string Scale);

public sealed record SampleRecord(
    string Id,
    string MaterialName,
    IReadOnlyList<DimValue> Values,   // 与维度定义一一对应；缺失值原样保留
    IReadOnlyList<IReadOnlyList<double?>>? Covariance,
    string CovarianceUnit,
    string BranchId,
    string PredecessorId,            // 同一材料重新解释 -> 新分支，旧分支继续可读
    string Note);

public sealed record EndmemberRecord(
    string Id,
    string Name,
    string MaterialName,
    string BranchId,
    string PredecessorId,
    IReadOnlyList<DimValue> Means,
    IReadOnlyList<IReadOnlyList<double?>>? Covariance, // 端元分布（多元正态近似）
    string CovarianceUnit,
    string Note);

public sealed record FractionationFactor(DimValue Mean, double? Sd);

public sealed record FractionationVersion(
    string Id,
    string Name,
    string PredecessorId,
    // 每个端元 x 每个维度的分馏因子（消费者相对食物源）
    IReadOnlyDictionary<string, IReadOnlyList<FractionationFactor>> ByEndmember,
    string Note);

public sealed record PriorSpec(
    string Id,
    string Name,
    string PredecessorId,
    IReadOnlyDictionary<string, double>? DirichletAlpha, // null = 均匀先验
    double? LowerBound, double? UpperBound,               // 先验为每个比例设定的硬边界
    string Note);

// ==================== 规范化层（派生层；失败保留诊断） ====================

public sealed record NormalizedSample(
    string Id, string MaterialName, string BranchId, string PredecessorId,
    double[] Vector, bool[] Present, double[,]? Covariance,
    IReadOnlyList<string> Diagnostics);

public sealed record NormalizedEndmember(
    string Id, string Name, string MaterialName, string BranchId, string PredecessorId,
    double[] Means, bool[] Present, double[,]? Covariance,
    IReadOnlyList<string> Diagnostics);

public sealed record NormalizedFractionation(
    string Id, string Name, string PredecessorId, double[,] Shift, bool[,] Present,
    IReadOnlyList<string> Diagnostics);

public sealed record NormalizedPrior(
    string Id, string Name, string PredecessorId,
    double[]? DirichletAlpha, double LowerBound, double UpperBound,
    IReadOnlyList<string> Diagnostics);

public sealed record Scenario(
    string SampleId,
    IReadOnlyList<string> EndmemberIds,
    string FractionationVersionId,
    string PriorId,
    IReadOnlyList<string> ActiveDimensions);

// ==================== 求解结果 ====================

public enum Identifiability { Identifiable, WeaklyIdentified, Insufficient }
public enum RegionKind { Empty, Point, Intervals, Polygon, Polytope }

public sealed record CandidatePolygon(IReadOnlyList<double[]> Vertices, RegionKind Kind);

public sealed record FeasibleRegion(
    RegionKind Kind,
    IReadOnlyList<double> Intervals,     // n==2: [low0,high0,low1,high1]；投影区间
    IReadOnlyList<double[]> Vertices,    // 单纯形上的可行点（2D 多边形顶点或高维采样）
    bool Disjoint,
    IReadOnlyList<CandidatePolygon> Components);

public sealed record ResidualPoint(double[] Observed, double[] Predicted, double[] Residual,
    double Mahalanobis2, IReadOnlyList<string> Dimensions);

public sealed record MixEvent(
    string Type, string Message, IReadOnlyDictionary<string, object?> Data);

public sealed record CandidateResult(
    string CandidateId,
    string ScenarioKey,
    IReadOnlyList<string> EndmemberIds,
    double[] BestFit,                    // 受约束 WLS 最佳比例（非负、和为 1；非截断归一化）
    double BestMahalanobis2,
    Identifiability Identifiability,
    double ConditionNumber,
    double SmallestCenteredSingular,
    FeasibleRegion Region,
    ResidualPoint Residual,
    bool PriorFeasible,
    IReadOnlyList<MixEvent> Events,
    string? BasisEventId);

public sealed record DerivationResult(
    string DerivationId,
    Scenario Scenario,
    IReadOnlyList<CandidateResult> Candidates,
    string Status,                        // feasible / infeasible / prior-infeasible / unidentifiable
    IReadOnlyList<MixEvent> Events,
    DateTimeOffset ComputedAt,
    string RuleVersion,
    string InputsFingerprint);

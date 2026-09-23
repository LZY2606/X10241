namespace IsoMix.Domain;

/// <summary>同位素维度定义，携带单位与坐标基准。</summary>
public sealed record IsotopeDim(string Name, string Unit, string Reference);

/// <summary>样品记录：原始同位素值，允许缺失维度。</summary>
public sealed record SampleRecord(
    string Id,
    string Name,
    Dictionary<string, double> Values,          // dimName -> value（原始单位）
    Dictionary<string, string> Units,           // dimName -> unit（原始表示）
    string RawPayload);

/// <summary>端元记录：均值 + 协方差，可多组分（混合分布 → 多个可行区）。</summary>
public sealed record EndmemberRecord(
    string Id,
    string Name,
    List<EndmemberComponent> Components,
    string RawPayload);

public sealed record EndmemberComponent(
    string Label,
    double Weight,
    Dictionary<string, double> Mean,
    double[][] Covariance,                      // 与 Dims 顺序一致
    List<string> Dims);

/// <summary>测量协方差（样品），相关误差按此传播。</summary>
public sealed record CovarianceRecord(
    string Id,
    List<string> Dims,
    double[][] Matrix,
    string RawPayload);

/// <summary>分馏因子版本。</summary>
public sealed record FractionationVersion(
    string Id,
    string Version,
    Dictionary<string, double> Factors,         // dimName -> Δ（加性修正，‰）
    string RawPayload);

/// <summary>先验假设：对某来源比例的下界约束。</summary>
public sealed record PriorSpec(
    string Id,
    List<PriorConstraint> Constraints,
    string RawPayload);

public sealed record PriorConstraint(string EndmemberId, double MinProportion);

/// <summary>分析输入快照（派生层的依赖集合）。</summary>
public sealed record AnalysisInput(
    SampleRecord Sample,
    List<EndmemberRecord> Endmembers,
    CovarianceRecord Covariance,
    FractionationVersion Fractionation,
    PriorSpec Prior,
    List<IsotopeDim> Dims,
    double ToleranceSigma);

public enum AnalysisStatus
{
    Ok,
    IdentifiabilityInsufficient,
    SampleOutsideEnvelope,
    MissingIsotopeDimension,
    MultipleFeasibleRegions,
    InfeasiblePrior,
    UnconvertibleUnit
}

/// <summary>机器可读取的判定依据。</summary>
public sealed record JudgmentEvent(
    string Rule,
    string Decision,
    Dictionary<string, object> Evidence);

/// <summary>一个合法候选（可行比例点）。约束通过受约束 QP 精确满足，绝不截断后归一化。</summary>
public sealed record Candidate(
    string Id,
    double[] Proportions,
    double MahalanobisD2,
    double[] Residuals,
    List<string> Dims,
    string RegionLabel);

/// <summary>可行区域（可能多个）。</summary>
public sealed record FeasibleRegion(
    string Label,
    List<double[]> GridPoints,                  // 单纯形网格上的可行点
    double[] BestFit);

public sealed record AnalysisResult(
    List<AnalysisStatus> Statuses,
    List<JudgmentEvent> Judgments,
    List<Candidate> Candidates,
    List<FeasibleRegion> Regions,
    List<string> Diagnostics,
    List<string> UsedDims,
    List<string> MissingDims,
    double ConditionNumber,
    string[] DependsOn);                        // 依赖的原始记录 id（失效范围计算用）

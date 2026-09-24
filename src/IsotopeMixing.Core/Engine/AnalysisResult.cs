using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Engine;

public enum Identifiability
{
    WellIdentified,
    WeaklyIdentified,
    Unidentifiable
}

public enum FeasibilityOutcome
{
    Empty,
    NonEmpty,
    PriorInfeasible
}

/// <summary>机器可读判定事件依据：每条判定一个稳定 code 与结构化 evidence。</summary>
public sealed record Judgment
{
    public required string Code { get; init; }
    public required string Severity { get; init; } // info | warning | error
    public required string Message { get; init; }
    public required IReadOnlyDictionary<string, double?> Metrics { get; init; }
    public string? RefObjectId { get; init; }
}

public sealed record Polygon2
{
    /// <summary>简化变量 (f0,f1,...,f_{m-2}) 平面上的凸多边形顶点，按顺序排列；空数组表示无可行域。</summary>
    public required IReadOnlyList<double[]> Vertices { get; init; }
    public required string[] ProportionOrder { get; init; }
}

public sealed record FeasibleRegion
{
    public required FeasibilityOutcome Outcome { get; init; }
    public required string HypothesisId { get; init; }
    public required string HypothesisLabel { get; init; }
    public required string[] ProportionOrder { get; init; }
    /// <summary>区域代表点（WLS 或投影最优点）；非唯一时不冒充单点答案。</summary>
    public double[]? RepresentativeProportions { get; init; }
    /// <summary>m<=3 且观测>=2 时给出凸多边形区域，否则给各比例的可行区间。</summary>
    public Polygon2? Polygon { get; init; }
    public double[]? MinProportions { get; init; }
    public double[]? MaxProportions { get; init; }
    public double? ChiSquareAtRepresentative { get; init; }
    public double ChiSquareThreshold { get; init; }
    public double[]? Residuals { get; init; }
    public string[]? ResidualTracerOrder { get; init; }
    public required IReadOnlyList<Judgment> Judgments { get; init; }
}

public sealed record CandidateSolution
{
    public required string CandidateId { get; init; }
    public required string HypothesisId { get; init; }
    public required string EndmemberSetVersionId { get; init; }
    public required string FractionationSetSignature { get; init; }
    public required FeasibleRegion Region { get; init; }
    public bool Adopted { get; set; }
    public string? DecisionEventId { get; set; }
}

public sealed record AnalysisResult
{
    public required string SampleId { get; init; }
    public required string[] ObservedTracerOrder { get; init; }
    public required Identifiability Identifiability { get; init; }
    public required string RuleVersion { get; init; }
    public required double ConfidenceLevel { get; init; }
    public required IReadOnlyList<CandidateSolution> Candidates { get; init; }
    public required IReadOnlyList<Judgment> SharedJudgments { get; init; }
    public bool MultipleDisjointRegions { get; init; }
}

using System.Text.Json.Nodes;

namespace IsotopeMix.Inference;

public static class EngineInfo
{
    public const string RuleVersion = "mix-engine-1.0.0";
}

public sealed class AssessmentRequest
{
    public required string SampleId { get; init; }
    public required NormalizedSample Sample { get; init; }
    public required IReadOnlyList<NormalizedSource> Sources { get; init; }
    public required NormalizedFractionation Fractionation { get; init; }
    public required Model.PriorDoc Prior { get; init; }
}

public sealed class FeasibleRegion
{
    public List<double[]> Vertices { get; set; } = new();
    public List<double[]> GridPoints { get; set; } = new();
    public double[]? Mean { get; set; }
    public double PosteriorWidth { get; set; }
}

public sealed class CandidateProportion
{
    public double[] Proportions { get; set; } = Array.Empty<double>();
    public double Mahalanobis2 { get; set; }
    public bool OnSimplex { get; set; }
}

public sealed class AssessmentResult
{
    public string EngineVersion { get; set; } = EngineInfo.RuleVersion;
    public string Status { get; set; } = "feasible"; // feasible | infeasible | unidentifiable | incomplete | prior_infeasible
    public List<string> Axes { get; set; } = new();
    public List<string> SourceIds { get; set; } = new();
    public List<string> SourceLabels { get; set; } = new();
    public double[]? UnconstrainedSolution { get; set; }
    public bool UnconstrainedOnSimplex { get; set; }
    public double[]? ConstrainedFit { get; set; }
    public double ConstrainedStatistic { get; set; }
    public double ThresholdStatistic { get; set; }
    public double[,]? ResidualCovariance { get; set; }
    public double[]? Residual { get; set; }
    public List<FeasibleRegion> Regions { get; set; } = new();
    public List<CandidateProportion> Candidates { get; set; } = new();
    public bool InEnvelope { get; set; }
    public bool HasMissingDimensions { get; set; }
    public bool ErrorCorrelated { get; set; }
    public bool IsIdentifiable { get; set; } = true;
    public double MinimumSingularValueRatio { get; set; }
    public List<JsonObject> Findings { get; set; } = new();
    public List<JsonObject> RawFindings { get; set; } = new();
    public List<AxisProblem> Problems { get; set; } = new();
    public double[,]? EndmemberMatrix { get; set; }
    public double[,]? SourceEnvelope { get; set; }
}

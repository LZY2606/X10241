using System.Text.Json.Serialization;

namespace IsotopeMix.Core.Domain;

public sealed class Finding
{
    [JsonPropertyName("code")] public string Code { get; init; } = "";
    [JsonPropertyName("severity")] public string Severity { get; init; } = "info";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
    [JsonPropertyName("detail")] public Dictionary<string, object?> Detail { get; init; } = new();

    public Finding(string code, string severity, string message, Dictionary<string, object?>? detail = null)
    {
        Code = code; Severity = severity; Message = message; Detail = detail ?? new();
    }
}

public static class FindingCodes
{
    public const string NearCollinear = "NEAR_COLLINEAR";
    public const string Underdetermined = "UNDERDETERMINED";
    public const string OutsideEnvelope = "OUTSIDE_ENVELOPE";
    public const string CorrelatedErrors = "CORRELATED_ERRORS";
    public const string MissingDimension = "MISSING_DIMENSION";
    public const string UnconvertibleValue = "UNCONVERTIBLE_VALUE";
    public const string FractionationVersionSwitch = "FRACTIONATION_VERSION_SWITCH";
    public const string MultipleFeasibleRegions = "MULTIPLE_FEASIBLE_REGIONS";
    public const string InfeasiblePrior = "INFEASIBLE_PRIOR";
    public const string CovarianceNotPositiveDefinite = "COVARIANCE_NOT_PD";
    public const string CovarianceMissing = "COVARIANCE_MISSING";
    public const string MultipleEndmemberModes = "MULTIPLE_ENDMEMBER_MODES";
}

public sealed class NormalizedQuantity
{
    public string DimKey { get; init; } = "";
    public double? Value { get; init; }
    public string Unit { get; init; } = "";
    public string? Raw { get; init; }
    public string? Diagnostic { get; init; }
    public bool Usable => Value.HasValue && string.IsNullOrEmpty(Diagnostic);
}

public sealed class NormalizedEndmemberMode
{
    public required string ModeId { get; init; }
    public string Label { get; init; } = "";
    public required Dictionary<string, NormalizedQuantity> Means { get; init; }
}

public sealed class NormalizedEndmember
{
    public required string Key { get; init; }
    public string Label { get; init; } = "";
    public required List<NormalizedEndmemberMode> Modes { get; init; }
}

public sealed class NormalizedSample
{
    public required string Key { get; init; }
    public string Label { get; init; } = "";
    public required Dictionary<string, NormalizedQuantity> Values { get; init; }
}

public sealed class NormalizedCovariance
{
    public required string SampleKey { get; init; }
    public required List<string> DimKeys { get; init; }
    public required double[][] Matrix { get; init; }
    public bool Correlated { get; init; }
}

public sealed class NormalizedFractionation
{
    public required string Key { get; init; }
    public string Label { get; init; } = "";
    /// <summary>shift[endmemberKey or "*"][dimKey] added to the effective source mean.</summary>
    public required Dictionary<string, Dictionary<string, NormalizedQuantity>> Shifts { get; init; }
}

public sealed class NormalizedPrior
{
    public required string Key { get; init; }
    public required string SampleKey { get; init; }
    public string Rationale { get; init; } = "";
    /// <summary>bounds[endmemberKey] = (lo, hi)</summary>
    public required Dictionary<string, (double Lo, double Hi)> Bounds { get; init; }
}

public sealed class NormalizedDataset
{
    public required List<DimensionDef> Dimensions { get; init; }
    public required List<NormalizedSample> Samples { get; init; }
    public required List<NormalizedEndmember> Endmembers { get; init; }
    public required List<NormalizedCovariance> Covariances { get; init; }
    public required List<NormalizedFractionation> Fractionations { get; init; }
    public required List<NormalizedPrior> Priors { get; init; }
}

public sealed record ResidualPoint(string DimKey, double Observed, double Predicted, double Sigma, double Standardized);

public sealed class FeasiblePolygon
{
    /// <summary>Barycentric triples for 3-endmember cases, ordered (e0,e1,e2); pairs for 2.</summary>
    public required List<double[]> Points { get; init; }
    public required Dictionary<string, double> Min { get; init; }
    public required Dictionary<string, double> Max { get; init; }
    public string Kind { get; init; } = "ternary-grid";
}

public sealed class MixingCandidate
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("sampleKey")] public required string SampleKey { get; init; }
    [JsonPropertyName("fractionationKey")] public required string FractionationKey { get; init; }
    [JsonPropertyName("priorKey")] public string? PriorKey { get; init; }
    [JsonPropertyName("modeSelection")] public required Dictionary<string, string> ModeSelection { get; init; }
    [JsonPropertyName("dimensionsUsed")] public required List<string> DimensionsUsed { get; init; }
    [JsonPropertyName("identifiable")] public required bool Identifiable { get; init; }
    [JsonPropertyName("conditionNumber")] public required double ConditionNumber { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("proportions")] public required Dictionary<string, double> Proportions { get; init; }
    [JsonPropertyName("mahalanobis2")] public required double Mahalanobis2 { get; init; }
    [JsonPropertyName("chi2Threshold")] public required double Chi2Threshold { get; init; }
    [JsonPropertyName("withinUncertainty")] public required bool WithinUncertainty { get; init; }
    [JsonPropertyName("residuals")] public required List<ResidualPoint> Residuals { get; init; }
    [JsonPropertyName("feasibleRegion")] public FeasiblePolygon? FeasibleRegion { get; init; }
    [JsonPropertyName("findings")] public required List<Finding> Findings { get; init; }
}

public sealed class SampleAnalysis
{
    [JsonPropertyName("sampleKey")] public required string SampleKey { get; init; }
    [JsonPropertyName("candidates")] public required List<MixingCandidate> Candidates { get; init; }
    [JsonPropertyName("findings")] public required List<Finding> Findings { get; init; }
}

public sealed class AnalysisResult
{
    [JsonPropertyName("ruleVersion")] public required string RuleVersion { get; init; }
    [JsonPropertyName("confidence")] public required double Confidence { get; init; }
    [JsonPropertyName("samples")] public required List<SampleAnalysis> Samples { get; init; }
    [JsonPropertyName("dependencies")] public required Dictionary<string, List<string>> Dependencies { get; init; }
}

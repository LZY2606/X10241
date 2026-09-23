using System.Text.Json.Serialization;

namespace IsotopeMixing.Solver;

/// <summary>An isotope system dimension, e.g. d13C VPDB.</summary>
public sealed record IsoAxis(string Key, string Label, string Unit, string Datum);

/// <summary>One endmember distribution: mean vector and covariance across isotope axes.</summary>
public sealed class EndmemberSource
{
    public required string Uid { get; init; }
    public required string Label { get; init; }
    public required double[] Mean { get; init; }
    public required double[][] Covariance { get; init; }
}

public sealed record EndmemberSet(string Uid, string Label, IReadOnlyList<EndmemberSource> Members, IReadOnlyList<IsoAxis> Axes);

/// <summary>Sample measurement: means with a (possibly correlated) measurement covariance. Null marks a missing dimension.</summary>
public sealed class SampleObservation
{
    public required string Uid { get; init; }
    public required string Label { get; init; }
    public required double?[] Values { get; init; }
    /// <summary>Covariance rows aligned with observed (non-null) values; null entries allowed for entirely missing rows.</summary>
    public required double?[][] Covariance { get; init; }
    public required string[] Units { get; init; }
    public required string[] Datums { get; init; }
}

public sealed record FractionationVersion(
    string Uid,
    string Label,
    /// <summary>Fractionation offsets indexed [endmember][axis]; added to endmember means.</summary>
    double[][] Offsets,
    IReadOnlyList<IsoAxis> Axes);

public sealed record PriorAssumption(
    string Uid,
    string Label,
    /// <summary>Allowed endmember indices per discrete structural group. Null groups => no structural ambiguity.</summary>
    IReadOnlyList<IReadOnlyList<int>>? StructuralGroups,
    /// <summary>Additional box bounds on weights per endmember: (min,max), null = [0,1].</summary>
    IReadOnlyList<(double Min, double Max)?>? WeightBoxes,
    string Note);

public sealed record MixingScenario(
    string ScenarioId,
    string Label,
    SampleObservation Sample,
    EndmemberSet Endmembers,
    FractionationVersion? Fractionation,
    PriorAssumption? Prior,
    double ConfidenceLevel);

public sealed record WeightPoint(double[] W, double Chi2);

public sealed record RegionPolygon(
    IReadOnlyList<double[]> Vertices,
    IReadOnlyList<string> EndmemberLabels,
    string Projection,
    double Area);

public sealed record RegionLine(IReadOnlyList<double[]> Points, IReadOnlyList<string> EndmemberLabels, string FreeUid, double Length);

public sealed record RegionUnresolved(
    IReadOnlyList<string> UnidentifiableEndmemberUids,
    IReadOnlyList<string> NullDirections,
    IReadOnlyList<double[]> WitnessWeights);

public sealed record AxisResidual(int AxisIndex, string AxisKey, double Observed, double Predicted, double Normalized, double Sigma);

public sealed record RuleAssessment(string Code, bool Satisfied, string Basis, string Evidence);

public sealed class MixingResult
{
    public required string Status { get; init; }            // feasible | infeasible | underidentified | unevaluable
    public required string Identifiability { get; init; }  // identified | weak | unidentifiable
    public required bool Feasible { get; init; }
    public int? Dof { get; init; }
    public double? Chi2 { get; init; }
    public double? PValue { get; init; }
    public double[]? BestWeights { get; init; }
    public IReadOnlyList<AxisResidual>? Residuals { get; init; }
    public RegionPolygon? Polygon { get; init; }
    public RegionLine? Line { get; init; }
    public RegionUnresolved? Unresolved { get; init; }
    public required IReadOnlyList<RuleAssessment> Assessments { get; init; }
    public string? StructuralGroupIndex { get; init; }
    public double ConfidenceLevel { get; init; }
    public string? InfeasibleReason { get; init; }
}

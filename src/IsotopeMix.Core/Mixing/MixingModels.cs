using System.Collections.Generic;

namespace IsotopeMix.Core.Mixing;

public sealed class MixingSource
{
    public required string SourceId { get; init; }
    public required string Label { get; init; }
    public required double[] Composition { get; init; }
    /// <summary>Endmember distribution covariance in the common isotope dimensions (null = treated as a fixed point).</summary>
    public double[][]? DistributionCovariance { get; init; }
}

public sealed class PriorBox
{
    public required string SourceId { get; init; }
    public double Lower { get; init; }
    public double Upper { get; init; } = 1.0;
}

public sealed class MixingInput
{
    public required string[] Dimensions { get; init; }
    public required string[] CanonicalBases { get; init; }
    public required IReadOnlyList<MixingSource> Sources { get; init; }
    /// <summary>Effective endmember matrix already including the selected fractionation version, shape k x q.</summary>
    public required double[][] EndmemberMatrix { get; init; }
    public required double[] Sample { get; init; }
    public required double[][] MeasurementCovariance { get; init; }
    public required IReadOnlyList<PriorBox> Priors { get; init; }
    /// <summary>Dimensions that existed in the raw records but could not participate (unparseable/unconvertible/missing).</summary>
    public required IReadOnlyList<DroppedDimension> DroppedDimensions { get; init; }
    public double ConfidenceLevel { get; init; } = 0.95;
    public int? GridResolution { get; init; }
}

public sealed record DroppedDimension(string Dimension, string Reason, string? TargetRef);

public sealed record JudgmentEvent(
    string Code,
    string Severity,
    string Message,
    IReadOnlyList<string> BasisRefs,
    IReadOnlyDictionary<string, object?> Payload);

public sealed record FeasiblePoint(
    double[] Proportions,
    double MahalanobisSquared,
    double[] Residuals,
    double[][] PropagatedCovariance,
    int ComponentId);

public sealed record FeasibleRegion(
    int ComponentId,
    double[] Centroid,
    double[] BestProportions,
    double[] SourceMin,
    double[] SourceMax,
    IReadOnlyList<double[]> HullVertices,
    int GridPointCount);

public sealed class MixingResult
{
    public required bool Feasible { get; init; }
    public required bool FeasibleUnderPriors { get; init; }
    public required double ChiSquareThreshold { get; init; }
    public required int DegreesOfFreedom { get; init; }
    public required int GridResolution { get; init; }
    public required IReadOnlyList<FeasiblePoint> Candidates { get; init; }
    public required IReadOnlyList<FeasibleRegion> Regions { get; init; }
    public required double[] ClosestProportions { get; init; }
    public required double ClosestMahalanobisSquared { get; init; }
    public required double[] ClosestResiduals { get; init; }
    public required double[] SourceRangeMin { get; init; }
    public required double[] SourceRangeMax { get; init; }
    public required double MinimumSingularValueRatio { get; init; }
    public required IReadOnlyList<double> NullSpaceDirections { get; init; }
    public required IReadOnlyList<JudgmentEvent> Events { get; init; }
}

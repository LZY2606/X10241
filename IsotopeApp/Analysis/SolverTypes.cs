namespace IsotopeApp.Analysis;

public sealed class SolverSource
{
    public required string SourceId { get; init; }
    public required string Name { get; init; }
    /// <summary>Mode means indexed [mode, tracer].</summary>
    public required double[][] ModeMeans { get; init; }
    /// <summary>Mode covariances indexed [mode].</summary>
    public required double[][,] ModeCovariances { get; init; }
    public required double[] ModeWeights { get; init; }
}

public sealed class SolverInput
{
    public required string TrialId { get; init; }
    public required IReadOnlyList<string> Tracers { get; init; }
    public required double[] Observation { get; init; }
    public required double[,] MeasurementCovariance { get; init; }
    public required IReadOnlyList<SolverSource> Sources { get; init; }
    /// <summary>Fractionation shifts applied to each tracer for every source.</summary>
    public required double[] FractionationShift { get; init; }
    public double ConfidenceLevel { get; init; } = 0.95;
    public int Subdivisions { get; init; }
    /// <summary>Bounds per source for the optional prior; null = unconstrained.</summary>
    public (double Low, double High)[]? PriorBounds { get; init; }
    public string? PriorKind { get; init; }
}

public sealed class FeasibleRegion
{
    public required int ComponentIndex { get; init; }
    public required double[] Center { get; init; }
    public required double[] MinProportion { get; init; }
    public required double[] MaxProportion { get; init; }
    public required int GridCellCount { get; init; }
    /// <summary>Best (minimum statistic) point within this region.</summary>
    public required double[] BestPoint { get; init; }
    public required double BestStatistic { get; init; }
    /// <summary>Which mode of each source explains this region (source -> mode).</summary>
    public required int[] ModeSelection { get; init; }
    public double RegionMass { get; init; }
}

public sealed class CandidateSolution
{
    public required string CandidateId { get; init; }
    public required int RegionIndex { get; init; }
    public required double[] Proportions { get; init; }
    public required double[] Residuals { get; init; }
    public required double Statistic { get; init; }
    public required int[] ModeSelection { get; init; }
    public required double[,] TotalCovariance { get; init; }
}

public sealed class SolverResult
{
    public required bool Feasible { get; init; }
    public required bool InsideMeanHull { get; init; }
    public required bool IdentifiabilityAdequate { get; init; }
    public required double ConditionNumber { get; init; }
    public required double SmallestSingularValue { get; init; }
    public required double StatisticThreshold { get; init; }
    public required double UnconstrainedBestStatistic { get; init; }
    public required IReadOnlyList<FeasibleRegion> Regions { get; init; }
    public required IReadOnlyList<CandidateSolution> Candidates { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required bool PriorInfeasible { get; init; }
    public required int GridSize { get; init; }
    public required int Subdivisions { get; init; }
    /// <summary>Per-source mode selected for each retained candidate.</summary>
    public required IReadOnlyList<int[]> CandidateModes { get; init; }
}

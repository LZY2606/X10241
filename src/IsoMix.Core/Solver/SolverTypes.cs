using System.Collections.Generic;

namespace IsoMix.Core.Solver;

public enum AnalysisStatus
{
    /// <summary>The constrained problem has at least one region inside the confidence set.</summary>
    Feasible = 0,

    /// <summary>
    /// Endmembers are (near) collinear / rank deficient: mixture proportions are
    /// not uniquely identifiable. All legal candidates are retained.
    /// </summary>
    InsufficientIdentifiability = 1,

    /// <summary>No legal proportions satisfy the constraints (e.g. sample outside envelope, infeasible prior).</summary>
    Infeasible = 2
}

public enum PriorFeasibility
{
    NotProvided = 0,
    Feasible = 1,
    Infeasible = 2,
    /// <summary>The prior is structurally invalid (weights not summing to one / negative), reported not repaired.</summary>
    Invalid = 3
}

public sealed record SolverOptions(
    double ConfidenceLevel = 0.95,
    int GridSteps = 48,
    double CollinearityThreshold = 0.05);

/// <summary>A connected component of the feasible grid, labeled by the endmember
/// distribution component combination that generated it.</summary>
public sealed record FeasibleRegion(
    string RegionId,
    string ComponentKey,
    int CellCount,
    double AreaFraction,
    double[] Centroid,
    double[] MinProportion,
    double[] MaxProportion,
    double BestMahalanobisSquared,
    double[] BestProportions,
    IReadOnlyList<double[]> HullPoints,
    IReadOnlyList<int[]> Cells);

public sealed record EnvelopeReport(
    bool InsideEnvelope,
    double NearestSquaredMahalanobis,
    double[] NearestProportions,
    string Note);

public sealed record DimensionUsage(
    string Isotope,
    bool Used,
    string Reason);

public sealed record MixingResult(
    AnalysisStatus Status,
    bool CorrelatedErrors,
    double SmallestSingularValue,
    double EffectiveRankMeasure,
    int EndmemberRank,
    int UsedDimensions,
    double ChiSquareThreshold,
    IReadOnlyList<FeasibleRegion> Regions,
    IReadOnlyList<double[]> AllCandidateProportions,
    EnvelopeReport Envelope,
    double[] ResidualVector,
    double[] BestProportions,
    double BestMahalanobisSquared,
    PriorFeasibility PriorFeasibility,
    string? PriorNote,
    IReadOnlyList<DimensionUsage> DimensionReport,
    IReadOnlyList<string> Findings);

public sealed record SolverInput(
    string SampleId,
    string[] SourceIds,
    string[] UsedIsotopes,
    /// <summary>Sample means in canonical per-mille, rows = used isotopes.</summary>
    double[] SampleMean,
    /// <summary>Sample covariance (canonical units), isotopes x isotopes.</summary>
    double[,] SampleCovariance,
    /// <summary>
    /// Effective endmember means after fractionation, [componentKey][isotype, source].
    /// </summary>
    IReadOnlyList<ComponentMeans> EndmemberVariants,
    /// <summary>Source covariance contribution per component variant, indexed like EndmemberVariants.</summary>
    IReadOnlyList<ComponentCovariance> EndmemberCovariance,
    IReadOnlyList<(string SourceId, double Weight)>? PriorWeights);

public sealed record ComponentMeans(string Key, double[,] Means);

public sealed record ComponentCovariance(string Key, double[,] Covariance);

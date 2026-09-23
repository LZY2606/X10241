namespace IsotopeMix.Core;

/// <summary>Machine-readable event types. Every judgment the system makes is one of these.</summary>
public static class EventTypes
{
    public const string ImportCommitted = "import.batch.committed";
    public const string ImportRolledBack = "import.batch.rolled-back";
    public const string BranchCreated = "branch.created";
    public const string DerivedComputed = "derived.computed";
    public const string DerivedInvalidated = "derived.invalidated";
    public const string IdentifiabilityInsufficient = "judgment.identifiability.insufficient";
    public const string FeasibleRegionFound = "judgment.feasible.region";
    public const string SampleOutsideEnvelope = "judgment.sample.outside-envelope";
    public const string PriorInfeasible = "judgment.prior.infeasible";
    public const string SimplexConstraintRespected = "judgment.constraint.simplex.respected";
    public const string CovariancePropagated = "judgment.covariance.propagated";
    public const string DimensionMissing = "diagnostic.dimension.missing";
    public const string UnitUnconvertible = "diagnostic.unit.unconvertible";
    public const string DecisionAccept = "decision.accept";
    public const string DecisionReject = "decision.reject";
    public const string DecisionAdjust = "decision.adjust";
    public const string DecisionUndo = "decision.undo";
    public const string VersionConflict = "version.conflict";
}

public sealed record RegionSummary(int Index, int PointCount, double VolumeFraction, double[] Centroid);

/// <summary>The derived, reviewable state of a case on a branch. Raw bytes stay untouched.</summary>
public sealed record DerivedState(
    string Key,
    string RulesVersion,
    IReadOnlyList<string> Axes,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Diagnostics,
    string Identifiability,
    double ConditionNumber,
    IReadOnlyList<RegionSummary> Regions,
    double[]? BestFeasible,
    double[]? ResidualAtBest,
    double MinMahalanobis,
    bool SampleOutsideEnvelope,
    bool PriorInfeasible,
    IReadOnlyList<(string Id, string Name, double[] Values)> EndmembersAdjusted,
    double[] SampleCanonical,
    double[,] CovarianceCanonical,
    bool Stale);

namespace IsotopeMixing.Core.Engine;

/// <summary>机器可读判定码（写入事件 evidence，长期稳定）。</summary>
public static class JudgmentCodes
{
    public const string EndmembersNearCollinear = "IDENTIFIABILITY.NEAR_COLLINEAR";
    public const string UnderdeterminedMissingDimension = "IDENTIFIABILITY.MISSING_DIMENSION";
    public const string SampleOutsideEnvelope = "GEOMETRY.SAMPLE_OUTSIDE_ENVELOPE";
    public const string CorrelatedErrorPropagated = "UNCERTAINTY.CORRELATED_COVARIANCE";
    public const string SimplexConstraintsEnforced = "CONSTRAINTS.SIMPLEX_ENFORCED_NO_TRUNCATION";
    public const string FeasibleRegionNonEmpty = "FEASIBILITY.NON_EMPTY";
    public const string FeasibleRegionEmpty = "FEASIBILITY.EMPTY";
    public const string PriorInfeasible = "PRIOR.INFEASIBLE";
    public const string MultipleDisjointRegions = "FEASIBILITY.MULTIPLE_DISJOINT_REGIONS";
    public const string FractionationVersionSwitch = "FRACTIONATION.VERSION_SWITCH";
    public const string UnconvertibleValueKept = "IMPORT.UNCONVERTIBLE_VALUE_KEPT";
    public const string NonUniqueCandidatesRetained = "CANDIDATES.ALL_LEGAL_RETAINED";
}

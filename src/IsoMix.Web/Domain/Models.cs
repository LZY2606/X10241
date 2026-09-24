namespace IsoMix.Web.Domain;

/// <summary>A quantity that always carries its unit and coordinate basis (e.g. delta notation reference).</summary>
public record Quantity(double Value, string Unit, string Basis);

/// <summary>An isotope dimension such as d13C. Basis is the reference material (VPDB, AIR-N2, VCDT).</summary>
public record IsotopeDim(string Key, string Unit, string Basis);

/// <summary>Raw record exactly as imported. The raw JSON body is never rewritten after import.</summary>
public record SourceRecord(string Id, int Seq, string Kind, string Name, string RawJson, string ContentHash, string ImportedAt);

public static class SourceKinds
{
    public const string Sample = "sample";
    public const string Endmember = "endmember";
    public const string Covariance = "covariance";
    public const string Fractionation = "fractionation";
    public const string Prior = "prior";
}

/// <summary>Normalized sample in the derived layer. Missing dims stay absent; unconvertible values are reported, not dropped silently.</summary>
public record NormSample(string SourceId, Dictionary<string, double> Values, List<string> MissingDims, List<string> Diagnostics);

public record NormEndmember(string SourceId, string Name, Dictionary<string, double> Mean);

public record NormCovariance(string SourceId, string[] Dims, double[][] Matrix);

public record NormFractionation(string SourceId, string Version, Dictionary<string, double> OffsetPerDim);

public record NormPrior(string SourceId, Dictionary<string, double> MinProportion);

/// <summary>One connected feasible region inside the proportion simplex.</summary>
public record CandidateRegion(int Index, double[] Centroid, double Weight, double MinMahalanobis);

public record Diagnostic(string Code, string Message, string? SourceId);

/// <summary>Result of one derivation. Flags and basis are machine-readable and stored with the event.</summary>
public record DerivationResult(
    string Status,
    List<string> Flags,
    List<CandidateRegion> Candidates,
    double[]? BestProportions,
    double[] Residuals,
    string[] ResidualDims,
    double MinMahalanobis,
    double ConditionNumber,
    int GridResolution,
    List<Diagnostic> Diagnostics,
    bool[] FeasibleGrid,
    int[] GridRegionIds)
{
    public const string Ok = "ok";
    public const string IdentifiabilityInsufficient = "identifiability-insufficient";
    public const string Infeasible = "infeasible";

    public const string FlagNearCollinear = "near-collinear-endmembers";
    public const string FlagOutsideEnvelope = "sample-outside-envelope";
    public const string FlagCorrelatedErrors = "correlated-errors-propagated";
    public const string FlagMissingDim = "missing-isotope-dimension";
    public const string FlagMultipleRegions = "multiple-feasible-regions";
    public const string FlagInfeasiblePrior = "infeasible-prior";
    public const string FlagFractionationSwitched = "fractionation-version-switched";
}

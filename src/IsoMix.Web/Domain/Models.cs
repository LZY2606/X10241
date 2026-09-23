namespace IsoMix.Domain;

public sealed record SampleInput(
    string Name,
    string[] Isotopes,
    double[] Values,
    string[] Units,
    double[][] Covariance,
    string CoordinateBasis);

public sealed record EndmemberInput(
    string Name,
    string Version,
    string[] Isotopes,
    double[] Mean,
    string[] Units,
    double[][] Covariance,
    string CoordinateBasis);

public sealed record FractionationInput(
    string Name,
    string Version,
    string[] Isotopes,
    double[] Delta,
    string[] Units);

public sealed record PriorInput(string Name, double[][] Intervals);

public sealed record CaseInput(
    string CaseId,
    string Title,
    SampleInput Sample,
    EndmemberInput[] Endmembers,
    FractionationInput Fractionation,
    PriorInput? Prior);

public sealed record CaseBundle(string CaseId, string Title, IReadOnlyList<CaseInput> Versions);

public sealed record Candidate(
    string Id,
    int Index,
    double[] Fractions,
    double[]? Interval,
    double[][]? Polygon,
    double Mahalanobis,
    double[] Residual,
    string Status);

public sealed record RunResult(
    string RunId,
    string Status,
    IReadOnlyList<string> Flags,
    IReadOnlyList<Candidate> Candidates,
    IReadOnlyList<EnvelopePoint> Envelope,
    IReadOnlyList<string> Diagnostics);

public sealed record EnvelopePoint(double X, double Y);

public static class RunStatus
{
    public const string Feasible = "feasible";
    public const string MultiRegion = "multi-region";
    public const string Infeasible = "infeasible";
    public const string IdentifiabilityInsufficient = "identifiability-insufficient";
}

public static class RunFlags
{
    public const string NearCollinear = "near-collinear-endmembers";
    public const string OutsideEnvelope = "sample-outside-envelope";
    public const string CorrelatedError = "correlated-error";
    public const string MissingIsotope = "missing-isotope-dimension";
    public const string FractionationSwitched = "fractionation-version-switched";
    public const string MultipleRegions = "multiple-feasible-regions";
    public const string PriorInfeasible = "prior-infeasible";
    public const string UnconvertibleValue = "unconvertible-value";
}

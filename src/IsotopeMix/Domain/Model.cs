using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

public enum SourceKind
{
    Sample, Endmember, MeasurementCovariance, Fractionation, Units, Prior
}

public enum CandidateStatus
{
    Feasible,
    Infeasible,
    OutsideEnvelope,
    NotIdentified,
    MissingDimension,
    InfeasiblePrior,
    UnconvertibleInput
}

public enum BranchKind { Baseline, Reinterpretation }

public enum Decision { Pending, Accepted, Rejected }

public sealed record Quantity(
    string Raw,
    double? Value,
    string Unit,
    string BaseUnit,
    bool Convertible,
    string? Diagnostic)
{
    public static Quantity Unconvertible(string raw, string unit, string diagnostic) =>
        new(raw, null, unit, unit, false, diagnostic);
}

public sealed record SourceRecord
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public required SourceKind Kind { get; init; }
    public required string MediaType { get; init; }
    public required byte[] RawBytes { get; init; }
    public required string RawSha256 { get; init; }
    public required int Seq { get; init; }
    public required string BranchId { get; init; }
    public bool Accepted { get; init; } = true;
    public string? RollbackReason { get; init; }
}

public sealed record ParseDiagnostic(
    string RecordCode, string Field, string Severity, string Message, string RawValue);

public sealed record SampleMaterial(
    string Id, string Code, string Name,
    IReadOnlyList<string> Tracers,
    IReadOnlyList<Quantity?> Readings,
    string SourceRecordId, string RawSha256);

public sealed record EndmemberMaterial(
    string Id, string Code, string SuiteCode, string Name,
    IReadOnlyList<string> Tracers,
    IReadOnlyList<Quantity?> Means,
    IReadOnlyList<double> Sds,
    string SourceRecordId, string RawSha256);

public sealed record CovarianceMatrix(
    string Id, string SampleCode,
    IReadOnlyList<string> Tracers,
    double[][] Matrix,
    string SourceRecordId, string RawSha256);

public sealed record UnitConversion(
    string QuantityType, string Unit, string BaseUnit, double Factor, double Offset, string? Reference);

public sealed record UnitsRecord(
    string Id, string Code,
    IReadOnlyList<UnitConversion> Conversions,
    string SourceRecordId, string RawSha256);

public sealed record FractionationVersion(
    string Id, string Code, string Label,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Deltas,
    string SourceRecordId, string RawSha256, int Revision);

public sealed record PriorGroup(IReadOnlyList<string> EndmemberCodes, IReadOnlyDictionary<string, (double Lo, double Hi)> Bounds);

public sealed record PriorAssumption(
    string Id, string Code, string Label,
    IReadOnlyList<PriorGroup> Groups,
    string SourceRecordId, string RawSha256, int Revision);

public sealed record Branch(
    string Id, string Code, string Name, BranchKind Kind,
    string? ParentBranchId, string? BaseEventId, long CreatedAtUnix, string Note);

public sealed record EventRecord
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required int Seq { get; init; }
    public required long AtUnix { get; init; }
    public required string Type { get; init; }
    public required string Summary { get; init; }
    public required string BasisJson { get; init; }
    public string? CandidateId { get; init; }
    public string? BaseRevisionEventId { get; init; }
    public ConflictInfo? Conflict { get; init; }
}

public sealed record ConflictInfo(string OtherEventId, string OtherDecision, string DifferenceJson);

public sealed record DerivedVersion
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required string EventId { get; init; }
    public required string Kind { get; init; }
    public required string RuleVersion { get; init; }
    public required string InputsJson { get; init; }
    public required string ArtifactJson { get; init; }
    public required int Seq { get; init; }
    public bool Stale { get; set; }
    public string? StaleReason { get; set; }
}

public sealed record Polygon2(IReadOnlyList<(double X, double Y)> Points, string CoordinateSystem);

public sealed record FeasibleComponent(
    int Index,
    IReadOnlyList<(double[] Proportions, double Chi2)> Boundary,
    double Area,
    string CoordinateSystem);

public sealed record CandidateResult
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required string SampleCode { get; init; }
    public required string SuiteCode { get; init; }
    public required string FractionationCode { get; init; }
    public required string PriorCode { get; init; }
    public required int GroupIndex { get; init; }
    public required IReadOnlyList<string> EndmemberCodes { get; init; }
    public required CandidateStatus Status { get; init; }
    public required string StatusReason { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required IReadOnlyList<int> ObservedDimensions { get; init; }
    public required IReadOnlyList<string> MissingTracers { get; init; }
    public required double[] BestProportions { get; init; }
    public required double BestChi2 { get; init; }
    public required double ThresholdChi2 { get; init; }
    public required int DegreesOfFreedom { get; init; }
    public required double PValue { get; init; }
    public required double[] Residuals { get; init; }
    public required double[] NormalizedResiduals { get; init; }
    public required double[][] CovarianceAtBest { get; init; }
    public required IReadOnlyList<FeasibleComponent> Components { get; init; }
    public required IdentifiabilityReport Identifiability { get; init; }
    public required EnvelopeReport Envelope { get; init; }
    public required string ConstraintMethod { get; init; }
    public required string RuleVersion { get; init; }
}

public sealed record IdentifiabilityReport(
    bool Structural,
    bool Weak,
    int Rank,
    int RequiredRank,
    IReadOnlyList<double> SingularValues,
    double ConditionNumber,
    string MatrixBasis,
    string Verdict);

public sealed record EnvelopeReport(
    bool Inside,
    double Distance,
    IReadOnlyList<(double X, double Y)> Hull,
    string CoordinateSystem,
    IReadOnlyList<string> PlotTracers);

using System.Text.Json.Serialization;

namespace IsotopeMixing.Web.Domain;

public sealed record CaseBundle(
    string CaseId,
    string Title,
    string PackageVersion,
    IReadOnlyList<IsotopeDimension> Dimensions,
    IReadOnlyList<SampleRecord> Samples,
    IReadOnlyList<EndmemberVersion> EndmemberVersions,
    IReadOnlyList<FractionationVersion> FractionationVersions,
    IReadOnlyList<RawRecordManifest> RawRecords);

public sealed record IsotopeDimension(string Id, string Label, string Unit, string Basis);

public sealed record RawValue(
    string Raw,
    double? Value,
    string Unit,
    string? Basis,
    bool Convertible,
    string? Diagnostic = null)
{
    public static RawValue Numeric(double value, string unit, string? basis = null) =>
        new(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), value, unit, basis, true);
}

public sealed record SampleRecord(
    string Id,
    string Label,
    string RecordId,
    IReadOnlyDictionary<string, RawValue> Observed,
    RawValue[][] Covariance,
    IReadOnlyList<string> MissingDimensions,
    string ActiveEndmemberVersionId,
    string ActiveFractionationVersionId,
    PriorAssumption Prior,
    RawValue? CollectionCoordinate,
    string Source,
    string CapturedAt,
    string UnitsAssumed,
    string PriorAssumptionText);

public sealed record EndmemberRecord(string Id, string Label, IReadOnlyDictionary<string, RawValue> Mean, RawValue[][] Covariance);

public sealed record EndmemberVersion(
    string Id,
    string Label,
    string RecordId,
    string Source,
    string CapturedAt,
    IReadOnlyList<EndmemberRecord> Endmembers);

public sealed record FractionationVersion(
    string Id,
    string Label,
    string RecordId,
    string Source,
    string CapturedAt,
    string Convention,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, RawValue>> EpsilonByEndmember);

public sealed record PriorAssumption(IReadOnlyList<PriorComponent> Components);

public sealed record PriorComponent(string Id, IReadOnlyDictionary<string, FractionBound> Bounds);

public sealed record FractionBound(double Minimum, double Maximum, string Unit = "fraction");

public sealed record RawRecordManifest(
    string Id,
    string Kind,
    string Source,
    string CapturedAt,
    string Representation,
    string UnitsAssumed,
    string PriorAssumptions,
    string JsonPointer);

public sealed record ArchiveBundle(
    string Format,
    DateTimeOffset ExportedAt,
    CaseBundle Case,
    IReadOnlyList<BranchRecord> Branches,
    IReadOnlyList<EventRecord> Events,
    IReadOnlyList<CandidateRecord> Candidates,
    IReadOnlyList<DecisionRecord> Decisions,
    IReadOnlyList<DependencyRecord> Dependencies,
    IReadOnlyList<BlobRecord> Blobs);

public sealed record BranchRecord(string Id, string? ParentBranchId, string Label, string HeadEventId, DateTimeOffset CreatedAt);

public sealed record EventRecord(
    string Id,
    string CaseId,
    string BranchId,
    string? ParentEventId,
    int Sequence,
    string Type,
    string Summary,
    string Status,
    string PayloadJson,
    string MachineBasisJson,
    DateTimeOffset CreatedAt,
    string? ConflictOfEventId);

public sealed record CandidateRecord(
    string Id,
    string CaseId,
    string BranchId,
    string SampleId,
    string EndmemberVersionId,
    string FractionationVersionId,
    string ResultJson,
    string EventId,
    string? SupersededByEventId);

public sealed record DecisionRecord(string EventId, string Action, string TargetCandidateId, string? TargetRegionId, string Note);

public sealed record DependencyRecord(
    long Id,
    string EventId,
    string BranchId,
    string Scope,
    string RecordId,
    string VersionId,
    string Kind);

public sealed record BlobRecord(string Id, string MediaType, string Sha256, byte[] Bytes);

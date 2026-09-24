namespace StableIsotopeMixing.Web.Data;

public sealed record RawRow(
    string Id,
    string Kind,
    int Version,
    string RawFormat,
    string RawJson,
    string UnitBasis,
    string ContentHash,
    string ProvenanceJson,
    string? CreatedEventId);

public sealed record BranchRow(
    string Id,
    string Name,
    string? ParentBranchId,
    string? BaseEventId,
    string? HeadEventId,
    string Status,
    string? CreatedEventId);

public sealed record EventRow(
    string Id,
    string BranchId,
    long Seq,
    string? PrevEventId,
    string Kind,
    string PayloadJson,
    string Actor,
    string CreatedAt);

public sealed record InterpretationRow(
    string Id,
    string BranchId,
    string Name,
    string ConfigJson,
    string CreatedEventId);

public sealed record AnalysisRow(
    string Id,
    string BranchId,
    string InterpretationId,
    string Status,
    string FlagsJson,
    string DiagnosticsJson,
    string DepsJson,
    string EventId);

public sealed record CandidateRow(
    string Id,
    string AnalysisId,
    string Label,
    string VerticesJson,
    string BestPointJson,
    double Area,
    double MinMahalanobis);

public sealed record InvalidationRow(
    string Id,
    string EventId,
    string BranchId,
    string TriggerJson,
    string AffectedJson);

public sealed record ConflictRow(
    string Id,
    string BranchId,
    string? BaseEventId,
    string HeadEventId,
    string AttemptedJson,
    string CreatedAt);

public sealed record DecisionState(string CandidateId, string Action, string EventId, string Actor, string? Note);

public sealed class ConcurrencyConflict : Exception
{
    public ConcurrencyConflict(string branchId, string? baseEventId, string headEventId)
        : base($"Branch '{branchId}' head moved: expected '{baseEventId}', actual '{headEventId}'.")
    {
        BranchId = branchId;
        BaseEventId = baseEventId;
        HeadEventId = headEventId;
    }

    public string BranchId { get; }
    public string? BaseEventId { get; }
    public string HeadEventId { get; }
}

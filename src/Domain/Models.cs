namespace IsotopeLab.Domain;

/// <summary>
/// An immutable, content-addressed source record. The payload keeps the original representation
/// (units, scales, labels, comments). Normalisation happens only in the derived analysis layer.
/// </summary>
public sealed class RawRecord
{
    public required string Id { get; init; }
    public required string FamilyId { get; init; }
    public required int Version { get; init; }
    public required string Kind { get; init; }
    public required string SourceName { get; init; }
    public required string ContentType { get; init; }
    public required string RawPayload { get; init; }
    public required string RawSha256 { get; init; }
    public required string ImportBatchId { get; init; }
    public required string BranchId { get; init; }
    public string? CaseId { get; init; }
    public string? CaseStage { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public bool Superseded { get; set; }
}

public sealed class ImportBatch
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required string Kind { get; init; } // import | case-stage | bundle
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Ref { get; init; }
    public string? Note { get; init; }
}

public sealed class Branch
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ParentBranchId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Description { get; init; }
    public bool Archived { get; set; }
}

/// <summary>Append-only event. Every judgement, import and parameter change is one immutable row.</summary>
public sealed class EventRow
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required string Type { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? ParentEventId { get; init; }
    public string? UndoesEventId { get; init; }
}

public sealed class Analysis
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required string Label { get; init; }
    public required string SampleRecordId { get; init; }
    public required string EndmemberSetRecordId { get; init; }
    public required string CovarianceRecordId { get; init; }
    public required string FractionationRecordId { get; init; }
    public string? PriorRecordId { get; init; }
    public required string ResultJson { get; set; }
    public required string ResultSha256 { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Status { get; set; } // valid | invalidated
    public string? SupersededByAnalysisId { get; set; }
    public string? InvalidationReason { get; set; }
}

public sealed class Decision
{
    public required string Id { get; init; }
    public required string BranchId { get; init; }
    public required string AnalysisId { get; init; }
    public required string CandidateId { get; init; }
    public required string Verdict { get; init; } // accepted | rejected | adjusted
    public string? Note { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? RevokedDecisionId { get; init; }
    public string? RevokeReason { get; set; }
}

public static class RecordKinds
{
    public const string Dimension = "dimension";
    public const string Sample = "sample";
    public const string Covariance = "covariance";
    public const string EndmemberSet = "endmember-set";
    public const string Fractionation = "fractionation";
    public const string Prior = "prior";
    public const string Note = "note";
}

public static class EventTypes
{
    public const string BatchImported = "batch.imported";
    public const string AnalysisRun = "analysis.run";
    public const string AnalysisInvalidated = "analysis.invalidated";
    public const string DecisionMade = "decision.made";
    public const string DecisionRevoked = "decision.revoked";
    public const string BranchCreated = "branch.created";
    public const string BranchForked = "branch.forked";
    public const string ConstraintRejected = "constraint.rejected";
    public const string Diagnosis = "diagnosis";
    public const string ParameterRevised = "parameter.revised";
    public const string Conflict = "conflict.detected";
}

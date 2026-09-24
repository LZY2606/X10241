namespace IsotopeMix.Domain;

public abstract record EventPayload(string Type);

public sealed record RawBatchImported(
    string BatchId,
    string Label,
    string Origin,
    string ImportedAt,
    string RawPayloadSha256,
    string RawPayloadBlobId,
    RawBatch Batch,
    List<Diagnostic> Diagnostics
) : EventPayload("raw-batch-imported");

public sealed record BranchCreated(
    string BranchId,
    string ParentBranchId,
    string BaseEventSeq,
    string Label,
    string Purpose,
    string BatchId,
    string SampleId,
    string EndmemberSetId,
    string CovarianceId,
    string FractionationId,
    string PriorId,
    string ModePolicy,
    string CreatedAt,
    string? Revises
) : EventPayload("branch-created");

public sealed record AnalysisComputed(
    string AnalysisId,
    string BranchId,
    string RuleVersion,
    string BatchId,
    string SampleId,
    string EndmemberSetId,
    string CovarianceId,
    string FractionationId,
    string PriorId,
    string DependencyFingerprint,
    MixingResult Result,
    string ComputedAt
) : EventPayload("analysis-computed");

public sealed record DecisionMade(
    string DecisionId,
    string BranchId,
    string AnalysisId,
    string CandidateId,
    string Outcome,      // accepted | rejected | adjusted
    string Comment,
    string At,
    long BaseVersion
) : EventPayload("decision-made");

public sealed record DecisionReverted(
    string BranchId,
    string RevertedDecisionId,
    string RestoredDecisionId,
    string At
) : EventPayload("decision-reverted");

public sealed record ParameterRevision(
    string RevisionId,
    string BranchId,
    string NewBranchId,
    string Kind,             // fractionation | prior
    string OldRefId,
    string NewRefId,
    string At,
    List<string> AffectedAnalysisIds,
    string Basis
) : EventPayload("parameter-revision");

public sealed record SourceRevision(
    string RevisionId,
    string BranchId,
    string NewBranchId,
    string ReplacedRefType,   // sample | endmember-set | covariance
    string OldRefId,
    string NewRefId,
    string BatchId,
    string At,
    List<string> AffectedAnalysisIds,
    string Basis
) : EventPayload("source-revision");

public sealed record InvalidationBulletin(
    string BulletinId,
    string At,
    string Cause,            // parameter-revision | source-revision
    string RevisionEventId,
    List<string> StaleAnalysisIds,
    List<string> AffectedBranches,
    string ScopeDescription
) : EventPayload("invalidation-bulletin");

public sealed record CounterexampleRecorded(
    string ArchiveId,
    string BaselineBatchId,
    string Scenario,
    string BranchId,
    string AnalysisId,
    string ExpectedVerdict,
    string ObservedVerdict,
    bool EffectBoundedCorrectly,
    string At,
    List<JudgmentEvent> Evidence
) : EventPayload("counterexample-recorded");

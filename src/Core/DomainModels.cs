namespace IsotopeMixing.Core;

public enum MaterialKind { Sample, EndmemberSet, Covariance, Fractionation, Priors, UnitsSpec }

/// <summary>原始层记录：字节、解析出的字段与诊断并列保存，导入不做不可逆清洗。</summary>
public sealed record RawDocument(
    string DocumentId,
    string MaterialId,
    string MaterialType,
    string Stage,
    string MediaType,
    string OriginalFilename,
    byte[] ContentBytes,
    string Sha256,
    DateTimeOffset ImportedAt,
    string ImportRef);

/// <summary>单格原始值与派生数值：派生失败时 Value 为 null 且诊断非空。</summary>
public sealed record RawValue(
    string ValueId,
    string MaterialId,
    string RecordKey,
    string Field,
    string RawText,
    string? UnitRaw,
    double? Value,
    string? UnitCanonical,
    string? Diagnostic)
{
    public bool Usable => Value is not null && Diagnostic is null;
}

public sealed record ParsedMaterial(
    string MaterialId,
    MaterialKind Kind,
    string Label,
    string Stage,
    string VersionOf,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RawValue> Values,
    IReadOnlyList<string> Diagnostics)
{
    public bool HasHardErrors => Diagnostics.Any(d =>
        d.StartsWith("unparseable", StringComparison.Ordinal) ||
        d.StartsWith("unconvertible", StringComparison.Ordinal));
}

public enum EventType
{
    CaseOpened,
    MaterialImported,
    ImportAttemptFailed,
    BranchCreated,
    AnalysisRecomputed,
    CandidateAccepted,
    CandidateRejected,
    ParameterAdjusted,
    AdjudicationUndone,
    MaterialAmended,
    InvalidationComputed,
    PriorOverridden,
    CaseAnnotated
}

public sealed record EventRecord(
    long Seq,
    string EventId,
    string BranchId,
    long BranchIndex,
    string? ParentEventId,
    EventType Type,
    string SubjectId,
    string PayloadJson,
    DateTimeOffset OccurredAt,
    string Actor,
    string? BasisRule,
    string? SnapshotRef)
{
    public long[] DependsOn { get; init; } = Array.Empty<long>();
}

public sealed record BranchInfo(
    string BranchId,
    string Name,
    string? ParentBranchId,
    string? ForkedFromEventId,
    DateTimeOffset CreatedAt,
    string CreatedFrom,
    string? HeadEventId,
    long HeadSeq);

public sealed record StoredAnalysis(
    string AnalysisId,
    string BranchId,
    long AtEventSeq,
    string SampleId,
    string EndmemberSetId,
    string FractionationVersion,
    string PriorsId,
    string CovarianceId,
    string ResultJson,
    string StatusSummary,
    DateTimeOffset ComputedAt)
{
    public string[] DependsOnMaterialIds { get; init; } = Array.Empty<string>();
    public bool Stale { get; init; }
    public string? StaleReason { get; init; }
}

using System;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Events;

/// <summary>
/// 追加式事件：任何视图选择/候选切换/接受/否定/参数调整都产生新事件，
/// 回到旧事件即恢复当时依赖版本与可见结论，绝不用最新结果覆盖历史。
/// </summary>
public sealed class IsotopeEvent
{
    public required string EventId { get; init; }
    public required string CaseId { get; init; }
    public required string Type { get; init; }
    public required string BranchId { get; init; }
    public required string[] ParentEventIds { get; init; }
    public required long Seq { get; init; }
    public required string Actor { get; init; }
    public required string OccurredAt { get; init; }
    public required string RuleVersion { get; init; }
    public required JsonElement Payload { get; init; }
    /// <summary>本事件依赖的对象版本（样品/端元/分馏/规则），用于失效范围计算。</summary>
    public required DependencyRef[] Dependencies { get; init; }
    public string? SupersedesEventId { get; init; }
}

public sealed record DependencyRef(string Kind, string ObjectId, string VersionId, string ContentHash)
{
    public string Signature => $"{Kind}:{ObjectId}@{VersionId}:{ContentHash[..8]}";
}

public static class EventKinds
{
    public const string CaseImported = "case.imported";
    public const string EntityVersionAdded = "entity.version_added";
    public const string AnalysisRecorded = "analysis.recorded";
    public const string CandidateDecision = "decision.candidate";
    public const string ParameterAdjusted = "decision.parameter_adjusted";
    public const string BranchCreated = "branch.created";
    public const string ConflictDetected = "conflict.detected";
    public const string InvalidationsMarked = "invalidations.marked";
    public const string DecisionUndone = "decision.undone";
}

public sealed record CandidateDecisionPayload
{
    public required string SampleId { get; init; }
    public required string CandidateId { get; init; }
    public required string Decision { get; init; } // accept | reject
    public string? Rationale { get; init; }
    public required string BasedOnEventId { get; init; }
}

public sealed record ParameterAdjustedPayload
{
    public required string SampleId { get; init; }
    public required string ChangeKind { get; init; } // confidence | prior | fractionation-switch
    public required string OldRef { get; init; }
    public required string NewRef { get; init; }
    public required string BasedOnEventId { get; init; }
    public required string NewAnalysisEventId { get; set; }
}

public sealed record InvalidationScope
{
    public required string ChangedObjectId { get; init; }
    public required string ChangeType { get; init; }
    public required string[] AffectedEventIds { get; init; }
    public required string[] UnaffectedEventIds { get; init; }
    public required string Reason { get; init; }
}

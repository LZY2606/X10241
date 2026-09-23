using System.Text.Json.Serialization;

namespace IsotopeMix.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventType
{
    BatchImported,
    ImportRolledBack,
    InterpretationCreated,
    AnalysisCreated,
    ParameterRevision,
    RuleVersionChanged,
    SourceRevision,
    CandidateSwitched,
    Accepted,
    Rejected,
    DecisionUndone,
    ConflictDetected,
    ReinterpretBranchCreated,
    InvalidationsComputed,
    DossierBaselineSaved,
    DossierChallengeSaved
}

/// <summary>
/// 机器可读的溯源事件。每个事件携带：
///  - 稳定身份（EventId = 分支内序号 + 内容哈希，跨导出/导入不变）；
///  - 父事件哈希（形成链，顺序由 BranchOrdinal 固定）；
///  - 受影响对象与依赖快照（回到该事件可恢复当时的可见结论）；
///  - findings[]：判断依据（可识别性不足、协方差传播、不可行先验…）。
/// </summary>
public sealed class ReviewEvent
{
    public string EventId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public long BranchOrdinal { get; set; }
    public string ParentEventId { get; set; } = "";
    public string ParentHash { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public EventType Type { get; set; }
    public string Summary { get; set; } = "";
    public string Actor { get; set; } = "reviewer";
    public DateTimeOffset At { get; set; }

    /// <summary>本次事件引用的原始字节（RawRecord.Id 列表）。</summary>
    public List<string> RawRefs { get; set; } = new();

    /// <summary>该事件时刻生效的依赖版本（端元版本、分馏版本、规则版本…）。</summary>
    public DependencySnapshot DependsOn { get; set; } = new();

    /// <summary>本次事件导致失效的运行/结论（局部失效范围，绝不全局替换）。</summary>
    public List<Invalidation> Invalidations { get; set; } = new();

    /// <summary>机器可读判断依据。</summary>
    public List<Finding> Findings { get; set; } = new();

    /// <summary>事件正文（JSON：候选、裁定理由、冲突双方等）。</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>乐观并发：创建该事件时分支头必须等于此值。</summary>
    public string ExpectedHeadHash { get; set; } = "";
}

public sealed record DependencySnapshot(
    string EndmemberVersion = "",
    string FractionationVersion = "",
    string RuleVersion = "",
    string ParameterSetId = "",
    IReadOnlyList<string>? RunIds = null)
{
    public List<string> RunIdsList { get; init; } = new(RunIds ?? Array.Empty<string>());
}

public sealed record Invalidation(
    string Scope,          // run / branch-view / candidate
    string TargetId,       // 被失效的运行 id
    string BecauseOf,      // 触发源（source id / 参数集 id / 规则版本）
    string Reason,         // 人类可读原因
    bool LocalOnly);       // true=只影响局部依赖链

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FindingCode
{
    UnderidentifiedNearCollinear,
    StructurallyUnderidentified,
    SimplexConstraintsExactProjection,
    NoClampRenormalize,
    CorrelatedCovariancePropagated,
    OutsideEnvelope,
    MissingIsotopeDimension,
    FractionationVersionSwitched,
    MultipleFeasibleRegions,
    InfeasiblePrior,
    FeasibleRegionComputed,
    DecisionAccepted,
    DecisionRejected,
    Conflict,
    LocalInvalidation,
    ImportRollbackAtomic
}

public sealed record Finding(
    FindingCode Code,
    string Severity,       // info / warning / error
    string Message,
    IReadOnlyDictionary<string, string>? Data = null)
{
    public Dictionary<string, string> DataMap { get; init; } = new(Data ?? new Dictionary<string, string>());
}

public sealed class Branch
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string MaterialKey { get; set; } = "";
    public string ParentBranchId { get; set; } = "";
    public long? ParentEventOrdinal { get; set; }
    public string HeadEventId { get; set; } = "";
    public string HeadHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsAdjudication { get; set; }
}

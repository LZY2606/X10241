using IsotopeMix.Core.Mixing;

namespace IsotopeMix.Core.Models;

/// <summary>一次混合推导（运行）：结果 JSON + 精确依赖坐标。</summary>
public sealed class MixingRun
{
    public string Id { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string CreatedByEventId { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public string EndmemberVersion { get; set; } = "";
    public string FractionationVersion { get; set; } = "";
    public string CovarianceId { get; set; } = "";
    public string PriorId { get; set; } = "";
    public string RuleVersion { get; set; } = "";
    public string ParameterSetId { get; set; } = "";
    public string SampleId { get; set; } = "";
    public bool Superseded { get; set; }
    public string? SupersededByRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public MixingResult? Parsed { get; set; }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ChallengeStatus
{
    Accepted,   // 被采纳：系统在该边界条件下的判定符合预期范围
    Rejected,   // 未采纳：判定未被限制在正确范围（留档，不删除）
    Pending
}

/// <summary>反例档案中的一次“只改动一个直接来源”的挑战。</summary>
public sealed class DossierChallenge
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DirectSourceChanged { get; set; } = "";  // 只允许一个
    public string ChangeDescription { get; set; } = "";
    public string ExpectedEffect { get; set; } = "";
    public string ExpectedScope { get; set; } = "";        // 正确的失效/影响范围
    public ChallengeStatus Status { get; set; }
    public string AdjudicationNote { get; set; } = "";
    public string BaselineRunId { get; set; } = "";
    public string ChallengeRunId { get; set; } = "";
    public string EventId { get; set; } = "";
    public List<string> AffectedObjectIds { get; set; } = new();
    public List<string> InputSnippets { get; set; } = new();
}

/// <summary>
/// “源组成室”反例档案：基线（全部原始记录）+ 边界挑战。
/// 被采纳与未采纳的结果都保留。
/// </summary>
public sealed class Dossier
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string BaselineBatchId { get; set; } = "";
    public string BaselineNote { get; set; } = "";
    public List<DossierChallenge> Challenges { get; set; } = new();
}

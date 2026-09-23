using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Domain;

/// <summary>
/// 机器可读取事件。身份(id)在内容确定后由内容哈希确定,
/// 因此导出→删除→重新导入后身份、顺序与来源关系保持稳定。
/// </summary>
public sealed record EventRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("caseId")] string CaseId,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("parentId")] string? ParentId,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("chainHash")] string ChainHash);

public static class EventTypes
{
    public const string RawImported = "raw.imported";                 // 基线/源数据改动(整批或单条)
    public const string InterpretationCreated = "interpretation.created";
    public const string FractionationSwitched = "fractionation.switched";
    public const string EndmemberVersionSwitched = "endmember_version.switched";
    public const string CandidateAdjudicated = "candidate.adjudicated";
    public const string DecisionReverted = "decision.reverted";
    public const string ParametersAdjusted = "parameters.adjusted";
    public const string Invalidation = "result.invalidated";          // 依赖变化的失效范围(不覆盖旧结果)
}

/// <summary>来源链指针: 每个派生结果都能反查材料版本、规则版本和决定。</summary>
public sealed record Provenance(
    [property: JsonPropertyName("sampleRecordId")] string SampleRecordId,
    [property: JsonPropertyName("sampleVersion")] string SampleVersion,
    [property: JsonPropertyName("endmemberRecordIds")] List<string> EndmemberRecordIds,
    [property: JsonPropertyName("endmemberVersions")] Dictionary<string, string> EndmemberVersions,
    [property: JsonPropertyName("covarianceRecordIds")] List<string> CovarianceRecordIds,
    [property: JsonPropertyName("fractionationRecordId")] string FractionationRecordId,
    [property: JsonPropertyName("fractionationVersion")] string FractionationVersion,
    [property: JsonPropertyName("priorRecordId")] string? PriorRecordId,
    [property: JsonPropertyName("priorVersion")] string? PriorVersion,
    [property: JsonPropertyName("basisEventIds")] List<string> BasisEventIds);

/// <summary>单个合法候选(多个可行区/多模态时全部保留, 不压缩成单点答案)。</summary>
public sealed class MixingCandidate
{
    [JsonPropertyName("candidateId")] public string CandidateId { get; set; } = "";
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("modeChoice")] public Dictionary<string, string> ModeChoice { get; set; } = new();
    [JsonPropertyName("proportions")] public Dictionary<string, double> Proportions { get; set; } = new();
    [JsonPropertyName("mahalanobis2")] public double Mahalanobis2 { get; set; }
    [JsonPropertyName("residual")] public Dictionary<string, double> Residual { get; set; } = new();
    [JsonPropertyName("feasible")] public bool Feasible { get; set; }
}

public sealed class FeasibleRegion
{
    [JsonPropertyName("candidateId")] public string CandidateId { get; set; } = "";
    [JsonPropertyName("connectedComponentCount")] public int ConnectedComponentCount { get; set; } = 1;
    [JsonPropertyName("areaFraction")] public double AreaFraction { get; set; }
    [JsonPropertyName("gridStep")] public double GridStep { get; set; }
    [JsonPropertyName("feasibleCells")] public List<GridCell> FeasibleCells { get; set; } = new();
    [JsonPropertyName("components")] public List<List<GridCell>> Components { get; set; } = new();
}

public sealed record GridCell(
    [property: JsonPropertyName("i")] int I,
    [property: JsonPropertyName("j")] int J,
    [property: JsonPropertyName("p0")] double P0,
    [property: JsonPropertyName("p1")] double P1,
    [property: JsonPropertyName("component")] int Component);

/// <summary>完整混合分析结论(被采纳与未采纳结果都保存)。</summary>
public sealed class MixingResult
{
    [JsonPropertyName("status")] public string Status { get; set; } = "ok"; // ok|underidentified|outside-envelope|missing-dimension|infeasible-prior|unconvertible
    [JsonPropertyName("identifiability")] public string Identifiability { get; set; } = "identified"; // identified|weak|rank-deficient
    [JsonPropertyName("conditionNumber")] public double? ConditionNumber { get; set; }
    [JsonPropertyName("smallSingularValue")] public double? SmallSingularValue { get; set; }
    [JsonPropertyName("insideEnvelope")] public bool? InsideEnvelope { get; set; }
    [JsonPropertyName("usedDimensions")] public List<string> UsedDimensions { get; set; } = new();
    [JsonPropertyName("missingDimensions")] public List<string> MissingDimensions { get; set; } = new();
    [JsonPropertyName("unconvertibleDimensions")] public List<string> UnconvertibleDimensions { get; set; } = new();
    [JsonPropertyName("correlatedErrors")] public bool CorrelatedErrors { get; set; }
    [JsonPropertyName("chiSquareThreshold")] public double ChiSquareThreshold { get; set; }
    [JsonPropertyName("candidates")] public List<MixingCandidate> Candidates { get; set; } = new();
    [JsonPropertyName("regions")] List<FeasibleRegion> Regions { get; set; } = new();
    [JsonPropertyName("acceptedCandidateId")] public string? AcceptedCandidateId { get; set; }
    [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
    [JsonPropertyName("diagnostics")] public List<Diagnostic> Diagnostics { get; set; } = new();
    [JsonPropertyName("provenance")] public Provenance? Provenance { get; set; }
}

public sealed record Diagnostic(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("severity")] string Severity,     // info|warning|error
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("affected")] List<string> Affected);

/// <summary>事件信封: 事件 + 当时可见结论快照 + 分支头版本。</summary>
public sealed record EventView(
    [property: JsonPropertyName("event")] EventRecord Event,
    [property: JsonPropertyName("result")] MixingResult? Result,
    [property: JsonPropertyName("branchHeadId")] string BranchHeadId);

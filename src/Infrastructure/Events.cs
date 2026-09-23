using System.Text.Json.Serialization;

namespace IsotopeMix.Infrastructure;

public static class EventTypes
{
    public const string CaseImported = "CASE_IMPORTED";
    public const string BranchCreated = "BRANCH_CREATED";
    public const string SampleRevised = "SAMPLE_REVISED";
    public const string FractionationRevised = "FRACTIONATION_REVISED";
    public const string AnalysisComputed = "ANALYSIS_COMPUTED";
    public const string JudgmentRecorded = "JUDGMENT_RECORDED";
    public const string DecisionReversed = "DECISION_REVERSED";
    public const string AnalysisInvalidated = "ANALYSIS_INVALIDATED";
}

/// <summary>机器可读的判断依据事件：规则代码、阈值、通过/不通过与证据。</summary>
public sealed record JudgmentSpec
{
    public required string RuleCode { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public required string Evidence { get; init; }
    public required string SolverVersion { get; init; }
    public double? Threshold { get; init; }
    public double? Observed { get; init; }
}

public sealed class EventRow
{
    public long Seq { get; set; }
    public string EventId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string? DependsOnEventId { get; set; }
    public string? BasisEventId { get; set; }
    public string Author { get; set; } = "local";
    public string CreatedAt { get; set; } = "";
}

public sealed class RawRow
{
    public string RawId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string StableId { get; set; } = "";
    public string OriginalJson { get; set; } = "{}";
    public string? BlobSha256 { get; set; }
    public string? SourceBranchId { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class BlobRow
{
    public string Sha256 { get; set; } = "";
    public string MediaType { get; set; } = "application/octet-stream";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
}

public sealed class BranchRow
{
    public string BranchId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentBranchId { get; set; } = "";
    public long HeadSeq { get; set; }
}

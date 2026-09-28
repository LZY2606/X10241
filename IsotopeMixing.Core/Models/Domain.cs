using System.Text.Json.Nodes;

namespace IsotopeMixing.Core.Models;

/// Immutable, content-addressed source record. Raw representation is preserved verbatim;
/// <see cref="Normalized"/> holds the derived (canonical-unit) layer only when convertible.
public sealed class SourceItem
{
    public string ItemId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Kind { get; set; } = ""; // sample | endmember | covariance | fractionation | units | prior
    public string Label { get; set; } = "";
    public int Version { get; set; }
    public string RawJson { get; set; } = "";
    public JsonNode? Normalized { get; set; }
    public bool Convertible { get; set; } = true;
    public string? Diagnostics { get; set; }
    public string Sha256 { get; set; } = "";
    public string ImportBatchId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Branch
{
    public string BranchId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentBranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Archived { get; set; }
}

/// Append-only event. Never updated; re-interpreting source material appends a new event
/// on a new branch. Optimistic concurrency uses Seq.
public sealed class EventRecord
{
    public long Seq { get; set; }
    public string EventId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string? ItemId { get; set; }
    public int? ItemVersion { get; set; }
    public string? RunId { get; set; }
    public string RulesVersion { get; set; } = "";
    public string? ParentEventId { get; set; }
    public string Actor { get; set; } = "reviewer";
    public string? Adjudication { get; set; } // accepted | rejected | superseded
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CaseRecord
{
    public string CaseId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string MainBranchId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// A computed feasible-region run. Status moves Current -> Stale when any declared
/// dependency changes; stale rows are never overwritten or globally replaced.
public sealed class RunRecord
{
    public string RunId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string SampleId { get; set; } = "";
    public string InputsJson { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public string RulesVersion { get; set; } = "";
    public string DependsJson { get; set; } = "[]"; // [{"itemId","version","sha256"}]
    public string Status { get; set; } = "Current"; // Current | Stale
    public string? StaleReason { get; set; }
    public string? Adjudication { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

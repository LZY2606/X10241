using System.Text.Json.Nodes;

namespace IsotopeMixing.Storage;

/// <summary>
/// Raw record exactly as supplied. rawText/rawJson are never rewritten by the system;
/// interpretation lives in separate derived rows.
/// </summary>
public sealed class RawRecordRow
{
    public required string Uid { get; set; }
    public required string BatchId { get; set; }
    public required string RecordKind { get; set; }
    public required int Ordinal { get; set; }
    public required string RawJson { get; set; }
    public required string RawText { get; set; }
    public string? Unit { get; set; }
    public string? Datum { get; set; }
    public required string ByteSha256 { get; set; }
    public required string ParseStatus { get; set; }
    public required string DiagnosticsJson { get; set; }
}

public sealed class EventRow
{
    public required string BranchId { get; set; }
    public required long Seq { get; set; }
    public required string EventId { get; set; }
    public required string At { get; set; }
    public required string EventType { get; set; }
    public required JsonNode Payload { get; set; }
    public string? ProducedResultId { get; set; }
    public string? UndoOfEventId { get; set; }
}

public sealed class ResultRow
{
    public required string ResultId { get; set; }
    public required string BranchId { get; set; }
    public required string CreatedEventId { get; set; }
    public required long CreatedSeq { get; set; }
    public required string ScenarioId { get; set; }
    public required string ScenarioLabel { get; set; }
    public string? SampleUid { get; set; }
    public required string EndmemberSetId { get; set; }
    public string? CovUid { get; set; }
    public string? FracVersionId { get; set; }
    public string? PriorId { get; set; }
    public string? Unit { get; set; }
    public string? Datum { get; set; }
    public required string Status { get; set; }
    public required string Identifiability { get; set; }
    public required bool Feasible { get; set; }
    public int? Dof { get; set; }
    public double? Chi2 { get; set; }
    public double? PValue { get; set; }
    public string? BestWeightsJson { get; set; }
    public string? ResidualJson { get; set; }
    public string? RegionJson { get; set; }
    public required string AssessmentsJson { get; set; }
    public required string InputsJson { get; set; }
    public bool Invalidated { get; set; }
    public string? InvalidatedEventId { get; set; }
    public string? InvalidationScope { get; set; }
}

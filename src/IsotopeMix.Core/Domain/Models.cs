using System.Text.Json.Serialization;

namespace IsotopeMix.Core.Domain;

/// <summary>A measured isotope dimension (tracer) with its coordinate/reference basis.</summary>
public sealed record DimensionDef(string Key, string Label, string CoordinateBasis, string Unit);

/// <summary>
/// A domain quantity. Every value entering the system keeps the original textual
/// representation and its unit. Values that cannot be normalized stay in place
/// with a diagnostic instead of being dropped.
/// </summary>
public sealed record Quantity
{
    [JsonPropertyName("value")] public double? Value { get; init; }
    [JsonPropertyName("unit")] public string Unit { get; init; } = "";
    [JsonPropertyName("raw")] public string? Raw { get; init; }
    [JsonPropertyName("diagnostic")] public string? Diagnostic { get; init; }

    public bool IsUsable => Value.HasValue && string.IsNullOrEmpty(Diagnostic);

    public static Quantity Of(double value, string unit, string? raw = null) =>
        new() { Value = value, Unit = unit, Raw = raw ?? value.ToString("G17"), Diagnostic = null };

    public static Quantity Unusable(string? raw, string unit, string diagnostic) =>
        new() { Value = null, Unit = unit, Raw = raw, Diagnostic = diagnostic };

    public static Quantity NotMeasured(string? raw = null) =>
        new() { Value = null, Unit = "", Raw = raw, Diagnostic = "not-measured" };
}

public static class RawKinds
{
    public const string Dimension = "dimension";
    public const string Sample = "sample";
    public const string Endmember = "endmember";
    public const string Covariance = "covariance";
    public const string Fractionation = "fractionation";
    public const string Prior = "prior";
    public const string Note = "note";
}

/// <summary>
/// An immutable raw record. Source material is append-only: a re-interpretation
/// creates a new versioned row for the same natural key; the old bytes stay readable.
/// </summary>
public sealed class RawRecord
{
    public string Id { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string NaturalKey { get; set; } = "";
    public int Version { get; set; }
    public string SupersedesId { get; set; } = "";
    public string ContentJson { get; set; } = "{}";
    public string Sha256 { get; set; } = "";
    public string Source { get; set; } = "";
    public string CreatedEventId { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class BranchRecord
{
    public string Id { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentBranchId { get; set; } = "";
    public string ForkedFromEventId { get; set; } = "";
    public string HeadEventId { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class EventRecord
{
    public string Id { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public int Seq { get; set; }
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public string ParentEventId { get; set; } = "";
    public string Author { get; set; } = "";
    public string RuleVersion { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class AnalysisRecord
{
    public string Id { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string EventId { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
    public string DependenciesJson { get; set; } = "[]";
    public string RuleVersion { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

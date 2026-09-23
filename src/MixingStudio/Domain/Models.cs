using System.Text.Json;
using System.Text.Json.Serialization;

namespace MixingStudio.Domain;

/// <summary>Kinds of raw source materials. Raw bytes are always preserved.</summary>
public static class MaterialKinds
{
    public const string Sample = "sample";
    public const string Endmembers = "endmembers";
    public const string Covariance = "covariance";
    public const string Fractionation = "fractionation";
    public const string Units = "units";
    public const string Prior = "prior";
}

public sealed record MaterialRecord(
    string Id, string Kind, int Version, string RawJson, string Sha256, string EventId);

public sealed record BranchRecord(
    string Id, string Name, string? ParentBranchId, string BaseEventId, long HeadSeq);

public sealed record EventRecord(
    string Id, string BranchId, long Seq, string Type, string PayloadJson,
    string MaterialRefsJson, string CreatedAt);

public sealed record CandidateRecord(
    string Id, string EventId, int RegionIndex, string PolygonJson,
    string Status, string? AdjudicationEventId);

public sealed record InvalidationRecord(
    string Id, string EventId, string TargetType, string TargetId, string Reason);

// ---------- typed payloads parsed from raw material JSON ----------

public sealed class SampleData
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("dims")] public List<string> Dims { get; set; } = new();
    [JsonPropertyName("values")] public List<double> Values { get; set; } = new();
    [JsonPropertyName("covariance")] public List<List<double>> Covariance { get; set; } = new();
    [JsonPropertyName("unit")] public string Unit { get; set; } = "permil";
}

public sealed class EndmemberData
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("dims")] public List<string> Dims { get; set; } = new();
    [JsonPropertyName("mean")] public List<double> Mean { get; set; } = new();
    [JsonPropertyName("covariance")] public List<List<double>> Covariance { get; set; } = new();
    [JsonPropertyName("unit")] public string Unit { get; set; } = "permil";
    [JsonPropertyName("states")] public List<EndmemberState>? States { get; set; }
}

public sealed class EndmemberState
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("mean")] public List<double> Mean { get; set; } = new();
    [JsonPropertyName("covariance")] public List<List<double>> Covariance { get; set; } = new();
}

public sealed class EndmemberSetData
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "v1";
    [JsonPropertyName("members")] public List<EndmemberData> Members { get; set; } = new();
}

public sealed class FractionationData
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "v1";
    [JsonPropertyName("dims")] public List<string> Dims { get; set; } = new();
    [JsonPropertyName("offsets")] public Dictionary<string, List<double>> OffsetsPerEndmember { get; set; } = new();
}

public sealed class PriorData
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("lower")] public List<double> Lower { get; set; } = new();
    [JsonPropertyName("upper")] public List<double> Upper { get; set; } = new();
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

public static class Json
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string To<T>(T value) => JsonSerializer.Serialize(value, Opts);
    public static T From<T>(string json) => JsonSerializer.Deserialize<T>(json, Opts)!;
}

using System.Text.Json.Serialization;

namespace IsotopeMix.Core.Domain;

public sealed class QuantityInput
{
    [JsonPropertyName("value")] public JsonScalar? Value { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
    [JsonPropertyName("basis")] public string? Basis { get; set; }
    [JsonPropertyName("sigma")] public double? Sigma { get; set; }
}

public sealed class JsonScalar
{
    public double? Number { get; init; }
    public string? Text { get; init; }
    public static JsonScalar? From(object? value) => null;
}

public sealed class DimensionPayload
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("coordinateBasis")] public string CoordinateBasis { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
}

public sealed class SamplePayload
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("values")] public Dictionary<string, QuantityInput> Values { get; set; } = new();
}

public sealed class EndmemberModePayload
{
    [JsonPropertyName("modeId")] public string ModeId { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("means")] public Dictionary<string, QuantityInput> Means { get; set; } = new();
}

public sealed class EndmemberPayload
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("modes")] public List<EndmemberModePayload> Modes { get; set; } = new();
}

public sealed class CovariancePayload
{
    [JsonPropertyName("sampleKey")] public string SampleKey { get; set; } = "";
    [JsonPropertyName("dims")] public List<string> Dims { get; set; } = new();
    [JsonPropertyName("matrix")] public List<List<double>> Matrix { get; set; } = new();
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
}

public sealed class FractionationPayload
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("shifts")]
    public Dictionary<string, Dictionary<string, QuantityInput>> Shifts { get; set; } = new();
}

public sealed class PriorBound
{
    [JsonPropertyName("lo")] public double Lo { get; set; }
    [JsonPropertyName("hi")] public double Hi { get; set; }
}

public sealed class PriorPayload
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("sampleKey")] public string SampleKey { get; set; } = "";
    [JsonPropertyName("rationale")] public string Rationale { get; set; } = "";
    [JsonPropertyName("bounds")] public Dictionary<string, PriorBound> Bounds { get; set; } = new();
}

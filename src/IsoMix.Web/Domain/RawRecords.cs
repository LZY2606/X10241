using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsoMix.Domain;

public sealed class RawObservation
{
    public string Isotope { get; set; } = "";
    public string? ValueText { get; set; }
    public string Unit { get; set; } = "";
    public string RawToken { get; set; } = "";
    public double? Value { get; set; }
}

public sealed class RawMatrix
{
    public string[][] Rows { get; set; } = Array.Empty<string[]>();
    public double?[][]? Values { get; set; }
}

public sealed class RawSample
{
    public string Name { get; set; } = "";
    public string CoordinateBasis { get; set; } = "";
    public List<RawObservation> Observations { get; set; } = new();
    public RawMatrix Covariance { get; set; } = new();
}

public sealed class RawEndmember
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string CoordinateBasis { get; set; } = "";
    public List<RawObservation> Observations { get; set; } = new();
    public RawMatrix Covariance { get; set; } = new();
}

public sealed class RawFractionationEntry
{
    public string Isotope { get; set; } = "";
    public string? ValueText { get; set; }
    public string Unit { get; set; } = "";
    public string RawToken { get; set; } = "";
    public double? Value { get; set; } = null;
}

public sealed class RawFractionation
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public List<RawFractionationEntry> Entries { get; set; } = new();
}

public sealed class RawPrior
{
    public string Name { get; set; } = "";
    public string[][] Intervals { get; set; } = Array.Empty<string[]>();
    public double?[][]? Values { get; set; }
}

public sealed class RawCase
{
    public string CaseId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Scenario { get; set; } = "";
    public RawSample Sample { get; set; } = new();
    public List<RawEndmember> Endmembers { get; set; } = new();
    public RawFractionation Fractionation { get; set; } = new();
    public RawPrior? Prior { get; set; }
}

public static class RawParse
{
    public static double? ParseNumber(object? token)
    {
        switch (token)
        {
            case null: return null;
            case double d: return d;
            case JsonElement je when je.ValueKind == JsonValueKind.Number:
                return je.GetDouble();
            default:
                var text = token.ToString()?.Trim();
                if (string.IsNullOrEmpty(text)) return null;
                return double.TryParse(text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }
    }
}

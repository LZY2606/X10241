using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsoMix.Domain;

/// <summary>Kinds of raw source material the archive understands.</summary>
public static class SourceKinds
{
    public const string Units = "units";
    public const string Sample = "sample";
    public const string Covariance = "covariance";
    public const string EndmemberSet = "endmember-set";
    public const string Fractionation = "fractionation";
    public const string Prior = "prior";
}

/// <summary>
/// A raw source record exactly as imported. <see cref="Raw"/> preserves the original
/// representation (bytes as JSON text); normalization happens only in the derived layer.
/// </summary>
public record SourceRecord(
    string Id,
    int Version,
    string Kind,
    string Label,
    string Raw,
    string Sha,
    string? Supersedes)
{
    public JsonNode Node => JsonNode.Parse(Raw)!;

    public static SourceRecord FromJson(string raw, string? supersedes = null)
    {
        var node = JsonNode.Parse(raw)!.AsObject();
        var id = node["id"]!.GetValue<string>();
        var version = node["version"]!.GetValue<int>();
        var kind = node["kind"]!.GetValue<string>();
        var label = node["label"]?.GetValue<string>() ?? id;
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return new SourceRecord(id, version, kind, label, raw, sha, supersedes);
    }
}

public record SampleDef(Dictionary<string, double?> Values, string? CovarianceRef, Dictionary<string, string> ValueUnits);

public record CovarianceDef(string[] Dims, double[][] Matrix);

public record EndmemberDef(string Key, Dictionary<string, double> Values, double[][] Cov);

public record EndmemberSetDef(List<EndmemberDef> Members);

public record FractionationDef(Dictionary<string, Dictionary<string, double>> Offsets);

public record PriorBox(double[] Lower, double[] Upper);

public record PriorDef(List<PriorBox> Boxes);

/// <summary>Typed accessors over raw source JSON. Parsing never mutates the stored raw text.</summary>
public static class SourceParser
{
    public static List<UnitsDim> ParseUnits(SourceRecord rec)
    {
        var dims = rec.Node["dimensions"]!.AsArray();
        return dims.Select(d => new UnitsDim(
            d!["name"]!.GetValue<string>(),
            d["unit"]!.GetValue<string>(),
            d["basis"]?.GetValue<string>() ?? "unspecified",
            d["standardRatio"]?.GetValue<double>() ?? 1.0)).ToList();
    }

    public static SampleDef ParseSample(SourceRecord rec)
    {
        var values = new Dictionary<string, double?>();
        foreach (var kv in rec.Node["values"]!.AsObject())
            values[kv.Key] = kv.Value is null ? null : kv.Value.GetValue<double>();
        var covRef = rec.Node["covariance"]?.GetValue<string>();
        var valueUnits = new Dictionary<string, string>();
        if (rec.Node["valueUnits"] is JsonObject vu)
            foreach (var kv in vu) valueUnits[kv.Key] = kv.Value!.GetValue<string>();
        return new SampleDef(values, covRef, valueUnits);
    }

    public static CovarianceDef ParseCovariance(SourceRecord rec)
    {
        var dims = rec.Node["dims"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        var matrix = rec.Node["matrix"]!.AsArray()
            .Select(row => row!.AsArray().Select(x => x!.GetValue<double>()).ToArray()).ToArray();
        return new CovarianceDef(dims, matrix);
    }

    public static EndmemberSetDef ParseEndmembers(SourceRecord rec)
    {
        var members = rec.Node["members"]!.AsArray().Select(m =>
        {
            var values = new Dictionary<string, double>();
            foreach (var kv in m!["values"]!.AsObject())
                values[kv.Key] = kv.Value!.GetValue<double>();
            var cov = m["cov"]!.AsArray()
                .Select(row => row!.AsArray().Select(x => x!.GetValue<double>()).ToArray()).ToArray();
            return new EndmemberDef(m["key"]!.GetValue<string>(), values, cov);
        }).ToList();
        return new EndmemberSetDef(members);
    }

    public static FractionationDef ParseFractionation(SourceRecord rec)
    {
        var offsets = new Dictionary<string, Dictionary<string, double>>();
        foreach (var kv in rec.Node["offsets"]!.AsObject())
        {
            var per = new Dictionary<string, double>();
            foreach (var dim in kv.Value!.AsObject())
                per[dim.Key] = dim.Value!.GetValue<double>();
            offsets[kv.Key] = per;
        }
        return new FractionationDef(offsets);
    }

    public static PriorDef ParsePrior(SourceRecord rec)
    {
        var boxes = rec.Node["boxes"]!.AsArray().Select(b =>
        {
            var lower = b!["lower"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
            var upper = b["upper"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
            return new PriorBox(lower, upper);
        }).ToList();
        return new PriorDef(boxes);
    }
}

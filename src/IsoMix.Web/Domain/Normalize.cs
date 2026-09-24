using System.Text.Json;

namespace IsoMix.Web.Domain;

/// <summary>
/// Derived-layer normalization. Raw records keep their original representation;
/// conversion happens only here. Values with unknown units stay in place (raw record
/// untouched) and produce diagnostics instead of being silently coerced.
/// </summary>
public static class Normalize
{
    public static bool TryToPermil(Quantity q, out double value, out string? diagnostic)
    {
        switch (q.Unit)
        {
            case "permil": value = q.Value; diagnostic = null; return true;
            case "percent": value = q.Value * 10.0; diagnostic = null; return true;
            default:
                value = 0;
                diagnostic = $"Unit '{q.Unit}' (basis {q.Basis}) cannot be converted to permil; value left in raw record only.";
                return false;
        }
    }

    public static bool TryToFraction(Quantity q, out double value, out string? diagnostic)
    {
        switch (q.Unit)
        {
            case "fraction": value = q.Value; diagnostic = null; return true;
            case "percent": value = q.Value / 100.0; diagnostic = null; return true;
            default:
                value = 0;
                diagnostic = $"Unit '{q.Unit}' cannot be converted to a proportion fraction; value left in raw record only.";
                return false;
        }
    }

    private static Dictionary<string, Quantity> ReadQuantities(JsonElement root, string property)
    {
        var map = new Dictionary<string, Quantity>();
        foreach (var kv in root.GetProperty(property).EnumerateObject())
        {
            var el = kv.Value;
            map[kv.Name] = new Quantity(
                el.GetProperty("value").GetDouble(),
                el.GetProperty("unit").GetString() ?? "",
                el.TryGetProperty("basis", out var b) ? b.GetString() ?? "" : "");
        }
        return map;
    }

    public static NormSample Sample(SourceRecord rec, IReadOnlyList<IsotopeDim> dims)
    {
        var root = JsonDocument.Parse(rec.RawJson).RootElement;
        var values = new Dictionary<string, double>();
        var diagnostics = new List<string>();
        foreach (var kv in ReadQuantities(root, "values"))
        {
            if (TryToPermil(kv.Value, out var v, out var diag)) values[kv.Key] = v;
            else diagnostics.Add($"{kv.Key}: {diag}");
        }
        var missing = dims.Select(d => d.Key).Where(key => !values.ContainsKey(key)).ToList();
        return new NormSample(rec.Id, values, missing, diagnostics);
    }

    public static NormEndmember Endmember(SourceRecord rec)
    {
        var root = JsonDocument.Parse(rec.RawJson).RootElement;
        var mean = new Dictionary<string, double>();
        foreach (var kv in ReadQuantities(root, "means"))
            if (TryToPermil(kv.Value, out var v, out _)) mean[kv.Key] = v;
        return new NormEndmember(rec.Id, rec.Name, mean);
    }

    public static NormCovariance Covariance(SourceRecord rec)
    {
        var root = JsonDocument.Parse(rec.RawJson).RootElement;
        var dims = root.GetProperty("dims").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var matrix = root.GetProperty("matrix").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(e => e.GetDouble()).ToArray()).ToArray();
        return new NormCovariance(rec.Id, dims, matrix);
    }

    public static NormFractionation Fractionation(SourceRecord rec)
    {
        var root = JsonDocument.Parse(rec.RawJson).RootElement;
        var offsets = new Dictionary<string, double>();
        foreach (var kv in ReadQuantities(root, "offsets"))
            if (TryToPermil(kv.Value, out var v, out _)) offsets[kv.Key] = v;
        return new NormFractionation(rec.Id, root.GetProperty("version").GetString() ?? "?", offsets);
    }

    public static NormPrior Prior(SourceRecord rec)
    {
        var root = JsonDocument.Parse(rec.RawJson).RootElement;
        var mins = new Dictionary<string, double>();
        foreach (var kv in ReadQuantities(root, "minProportion"))
            if (TryToFraction(kv.Value, out var v, out _)) mins[kv.Key] = v;
        return new NormPrior(rec.Id, mins);
    }
}

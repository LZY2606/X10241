using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IsotopeMixing.Domain;

/// <summary>
/// 单位 / 坐标基准处理。规范化（delta: per-mil→per-ten）只在派生层发生；
/// 无法识别或无法转换的量保留原值并带诊断，绝不丢弃。
/// </summary>
public static partial class Units
{
    public static string CanonicalIsotopeUnit(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "per-mil" or "permil" or "‰" or "ppt" => "per-ten",
        "per-ten" or "per1000" => "per-ten",
        "fraction" => "fraction",
        null => "?",
        var u => u
    };

    /// <summary>把原始量转成规范单位数值；失败返回带诊断的 RawQuantity（值仍保留在 Raw）。</summary>
    public static RawQuantity Normalize(string? rawText, double? value, string? unit)
    {
        if (value is null)
            return new RawQuantity(rawText, null, unit, "missing isotope dimension");

        var u = unit?.Trim().ToLowerInvariant();
        return u switch
        {
            "per-mil" or "permil" or "‰" or "ppt" or "per1000" =>
                new RawQuantity(rawText, value, "per-ten", null),
            "per-ten" => new RawQuantity(rawText, value, "per-ten", null),
            "fraction" => new RawQuantity(rawText, value * 1000.0, "per-ten", "converted from fraction"),
            null => new RawQuantity(rawText, value, null, "unit not declared; kept raw"),
            _ => new RawQuantity(rawText, value, u, $"unconvertible unit '{unit}'; kept in place")
        };
    }

    public static RawQuantity ReadIsotope(JsonObject iso)
    {
        var raw = iso["raw"]?.ToString();
        double? val = iso["value"] is JsonValue v && v.TryGetValue<double>(out var d) ? d
                     : iso["value"] is JsonValue vs && vs.TryGetValue<string>(out var s) &&
                       double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd) ? pd
                     : null;
        var unit = iso["unit"]?.GetValue<string>();
        return Normalize(raw, val, unit);
    }

    public static bool Convertible(RawQuantity q) =>
        q.Value is not null && (q.Unit == "per-ten" || q.Unit == "fraction");

    [GeneratedRegex(@"^[\s\d\.\+\-eE,]+$")]
    private static partial Regex Numericish();
}

/// <summary>把样品/端元记录解析为求解器输入；缺失或不可转换的维度被显式跳过并记录诊断。</summary>
public static class IsoReader
{
    public static IReadOnlyDictionary<string, RawQuantity> ReadIsotopes(JsonObject payload)
    {
        var result = new Dictionary<string, RawQuantity>();
        if (payload["isotopes"] is not JsonObject isotopes) return result;
        foreach (var kv in isotopes)
        {
            result[kv.Key] = kv.Value is JsonObject o ? Units.ReadIsotope(o) : RawQuantity.Missing;
        }
        return result;
    }

    public static List<string> CanonicalDims(IEnumerable<IReadOnlyDictionary<string, RawQuantity>> sources)
    {
        var dims = new List<string>();
        foreach (var src in sources)
            foreach (var (dim, q) in src)
                if (Units.Convertible(q) && !dims.Contains(dim)) dims.Add(dim);
        dims.Sort(StringComparer.Ordinal);
        return dims;
    }
}

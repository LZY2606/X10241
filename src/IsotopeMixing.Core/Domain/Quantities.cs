using System.Globalization;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Domain;

/// <summary>带单位的量。原始表示原样保留(OriginalText/OriginalUnit), 派生层才规范化。</summary>
public sealed record Quantity(
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("originalText")] string? OriginalText = null,
    [property: JsonPropertyName("originalUnit")] string? OriginalUnit = null,
    [property: JsonPropertyName("datum")] string? Datum = null)
{
    [JsonIgnore] public bool IsConvertible => Value.HasValue;
    [JsonIgnore] public string Display => OriginalText ?? Value?.ToString("G8", CultureInfo.InvariantCulture) ?? "?";
}

/// <summary>单位换算工具: 仅支持规范白名单内的换算; 无法转换的值留在原位置并附诊断。</summary>
public static class UnitConverter
{
    // 千分比/百分数; delta 值是相对于标准的比值, ppm↔ppth 等为纯比例换算
    private static readonly Dictionary<string, double> RatioFactors = new()
    {
        ["permille"] = 1.0, ["per_mille"] = 1.0, ["\u2030"] = 1.0,
        ["percent"] = 10.0, ["%"] = 10.0,
        ["ppm"] = 0.001, ["ppb"] = 0.000001
    };

    public static bool CanConvert(string from, string to) =>
        Normalize(from) == Normalize(to) ||
        (RatioFactors.ContainsKey(Normalize(from)) && RatioFactors.ContainsKey(Normalize(to)));

    public static double Convert(double value, string from, string to)
    {
        from = Normalize(from); to = Normalize(to);
        if (from == to) return value;
        if (RatioFactors.TryGetValue(from, out var f) && RatioFactors.TryGetValue(to, out var t))
            return value * f / t;
        throw new InvalidOperationException($"无法在单位 {from} 与 {to} 之间换算");
    }

    public static Quantity Normalize(Quantity q)
    {
        if (!q.Value.HasValue) return q;
        var target = TargetUnit(q.Unit);
        if (target is null || !CanConvert(q.Unit, target)) return q;
        return q with { Value = Convert(q.Value.Value, q.Unit, target), Unit = target };
    }

    /// <summary>delta 量统一到 permille; 其它单位不自动改名。</summary>
    private static string? TargetUnit(string unit)
    {
        var u = Normalize(unit);
        return RatioFactors.ContainsKey(u) ? "permille" : null;
    }

    private static string Normalize(string u) => (u ?? "").Trim().ToLowerInvariant().Replace(" ", "");
}

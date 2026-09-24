namespace IsotopeMixing.Core;

/// <summary>
/// 所有领域量携带单位或坐标基准。派生层只在显式可转换的同量纲单位间规范化；
/// 无法转换的值保留原始表示并附诊断，绝不为了画图而丢弃。
/// </summary>
public static class Units
{
    // 同量纲线性换算：canonical = factor*raw + offset
    private static readonly Dictionary<string, (string Canonical, double Factor, double Offset)> Linear =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["permil"] = ("permil", 1, 0),
            ["‰"] = ("permil", 1, 0),
            ["vpdb_permil"] = ("permil", 1, 0),
            ["vsmow_permil"] = ("permil", 1, 0),
            ["fraction"] = ("permil", 1000, 0),
            ["ratio_1e-3"] = ("permil", 1, 0),
        };

    public static bool IsKnown(string? unit) => unit is not null && Linear.ContainsKey(unit);

    public static (double value, string canonical, string? error) Convert(string? rawText, string? unit)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return (0, Canonical(unit), "missing");
        if (!double.TryParse(rawText.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
            return (0, Canonical(unit), $"unparseable-numeric:{rawText}");
        if (unit is null)
            return (v, "", "unit-undeclared");
        if (!Linear.TryGetValue(unit.Trim(), out var map))
            return (v, unit.Trim(), $"unconvertible-unit:{unit}");
        return (map.Factor * v + map.Offset, map.Canonical, null);
    }

    public static string Canonical(string? unit) =>
        unit is not null && Linear.TryGetValue(unit.Trim(), out var map) ? map.Canonical : (unit ?? "");

    /// <summary>同一规范量纲内可直接比较。</summary>
    public static bool SameBasis(string? a, string? b) =>
        string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>坐标基准（样品位置）；非数值或未知基准保留诊断。</summary>
public sealed record CoordPoint(double? Lon, double? Lat, string RawText, string? Diagnostic)
{
    public static CoordPoint Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new(null, null, "", "missing");
        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double lon) &&
            double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double lat))
            return new(lon, lat, raw, null);
        return new(null, null, raw, "unparseable-coordinate");
    }
}

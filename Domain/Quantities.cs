using System.Globalization;
using System.Text.Json;

namespace IsotopeMix.Domain;

/// <summary>
/// 领域量：始终携带单位与坐标基准（标准），并保留导入时的原始表示。
/// 派生层只读取 NumericValue/CanonicalPermille；无法转换的值留在原位并附诊断，绝不为画图丢弃。
/// </summary>
public sealed record Quantity
{
    public required string Raw { get; init; }
    public string? Unit { get; init; }
    public string? Datum { get; init; }
    public double? Value { get; init; }
    public string? Diagnostic { get; init; }

    public bool IsUsable => Value is not null && Diagnostic is null;

    public static Quantity FromJson(JsonElement el, string axisKey, string? expectedUnit, string? expectedDatum)
    {
        if (el.ValueKind == JsonValueKind.Null)
        {
            return new Quantity
            {
                Raw = "null",
                Unit = expectedUnit,
                Datum = expectedDatum,
                Diagnostic = $"axis '{axisKey}': value missing in source record"
            };
        }

        string raw;
        string? unit = expectedUnit;
        string? datum = expectedDatum;
        double? numeric = null;
        string? diagnostic = null;

        if (el.ValueKind == JsonValueKind.Number)
        {
            numeric = el.GetDouble();
            raw = numeric.Value.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            raw = el.TryGetProperty("raw", out var r) ? r.ToString() : el.GetRawText();
            if (el.TryGetProperty("unit", out var u) && u.ValueKind != JsonValueKind.Null) unit = u.GetString();
            if (el.TryGetProperty("datum", out var d) && d.ValueKind != JsonValueKind.Null) datum = d.GetString();
            if (el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number) numeric = v.GetDouble();
            if (numeric is null && double.TryParse(raw.TrimEnd('‰').Replace("o/oo", "").Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                numeric = parsed;
            }
        }
        else
        {
            raw = el.ToString();
        }

        if (numeric is null)
        {
            diagnostic = $"axis '{axisKey}': cannot parse numeric value from '{raw}'";
        }
        else
        {
            var unitError = UnitNormalizer.CheckUnit(axisKey, unit, expectedUnit);
            if (unitError is not null) diagnostic = unitError;
            var datumError = UnitNormalizer.CheckDatum(axisKey, datum, expectedDatum);
            if (datumError is not null)
                diagnostic = diagnostic is null ? datumError : diagnostic + "; " + datumError;
        }

        return new Quantity { Raw = raw, Unit = unit, Datum = datum, Value = numeric, Diagnostic = diagnostic };
    }
}

/// <summary>单位/基准白名单：只在派生层规范化，不匹配时给出诊断而非静默换算。</summary>
public static class UnitNormalizer
{
    private static readonly HashSet<string> PermilleSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "permille", "permil", "per-mille", "‰", "%o", "o/oo"
    };

    public static string? CheckUnit(string axisKey, string? actual, string? expected)
    {
        if (expected is null) return null;
        if (actual is null) return $"axis '{axisKey}': missing unit (expected '{expected}')";
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return null;
        if (PermilleSynonyms.Contains(expected) && PermilleSynonyms.Contains(actual)) return null;
        return $"axis '{axisKey}': unit '{actual}' cannot be converted to '{expected}'";
    }

    public static string? CheckDatum(string axisKey, string? actual, string? expected)
    {
        if (expected is null) return null;
        if (actual is null) return $"axis '{axisKey}': missing coordinate datum (expected '{expected}')";
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return $"axis '{axisKey}': datum '{actual}' does not match reference '{expected}'";
        }
        return null;
    }
}

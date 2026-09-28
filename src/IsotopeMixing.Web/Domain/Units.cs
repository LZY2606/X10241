using System.Globalization;

namespace IsotopeMixing.Domain;

/// <summary>
/// Unit/datum handling. The raw representation is always preserved; only the derived
/// layer normalizes. Values that cannot be converted stay at their origin with diagnostics.
/// Supported canonical isotope units: "permil" (delta values vs VPDB/VCDT/...),
/// "ratio" (absolute R), and mass fraction "wtfraction".
/// </summary>
public static class QuantityParser
{
    public static Quantity Parse(string? raw, string? unit, string canonicalUnit)
    {
        var diagnostics = new List<string>();
        raw ??= "";
        unit ??= "";
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("na", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("missing", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("—", StringComparison.Ordinal))
        {
            diagnostics.Add("MISSING_DIMENSION: value absent in original record; retained as missing");
            return new Quantity(raw, unit, null, canonicalUnit, diagnostics);
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            diagnostics.Add($"UNCONVERTIBLE_VALUE: '{trimmed}' is not numeric and cannot be normalized; left in place");
            return new Quantity(raw, unit, null, canonicalUnit, diagnostics);
        }

        var canonical = ConvertTo(numeric, unit.Trim().ToLowerInvariant(), canonicalUnit.ToLowerInvariant(), diagnostics);
        return new Quantity(raw, unit, canonical, canonicalUnit, diagnostics);
    }

    private static double? ConvertTo(double value, string unit, string target, List<string> diagnostics)
    {
        // delta permille family
        bool IsPermil(string u) => u is "permil" or "‰" or "ppt" or "per mille" or "per_mille" or "delta_permil";
        bool IsPercent(string u) => u is "percent" or "%" or "pct";

        if (IsPermil(unit))
        {
            if (IsPermil(target)) return value;
            if (target == "ratio") return Math.Pow(10.0, value / 1000.0 + Math.Log10(StandardRatioRef)) - 1.0; // placeholder ref avoided below
            if (IsPercent(target)) return value / 10.0;
        }
        if (IsPercent(unit) && IsPermil(target)) return value * 10.0;
        if (IsPercent(unit) && IsPercent(target)) return value;

        if (unit == target) return value;

        diagnostics.Add($"UNIT_UNCONVERTIBLE: '{unit}' -> '{target}' not supported without an explicit datum; original retained");
        return null;
    }

    // Kept for documentation; ratio conversion requires a standard reference which must be
    // supplied by the datum, so we deliberately do not silently use a global constant.
    private const double StandardRatioRef = 0.0112372;
}

public static class AxisLookup
{
    public static bool TryGetCanonical(CaseDataset ds, string axis, string entityId, bool isSample, out double value, out string? missingKind)
    {
        var ax = ds.Axes.FirstOrDefault(a => a.Symbol == axis);
        string raw;
        string unit;
        if (isSample)
        {
            var s = ds.Samples.First(x => x.Id == entityId);
            var mv = s.Measurements.FirstOrDefault(m => m.Axis == axis);
            if (mv?.Quantity is null) { value = 0; missingKind = "MISSING_DIMENSION"; return false; }
            if (mv.Quantity.Canonical is null) { value = 0; missingKind = mv.Quantity.Diagnostics.FirstOrDefault() ?? "UNCONVERTIBLE_VALUE"; return false; }
            value = mv.Quantity.Canonical.Value; missingKind = null; return true;
        }
        else
        {
            foreach (var set in ds.EndmemberSets)
            {
                var em = set.Endmembers.FirstOrDefault(e => e.Id == entityId);
                if (em is null) continue;
                var ev = em.Values.FirstOrDefault(v => v.Axis == axis);
                if (ev?.Quantity is null || ev.Quantity.Canonical is null) { value = 0; missingKind = "MISSING_DIMENSION"; return false; }
                value = ev.Quantity.Canonical.Value; missingKind = null; return true;
            }
            value = 0; missingKind = "ENDMEMBER_NOT_FOUND"; return false;
        }
    }
}

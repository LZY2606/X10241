namespace IsotopeMixing.Cases;

/// <summary>
/// Unit/datum handling: original representation is always retained on the raw record.
/// Only conversions we can perform transparently are normalized for the derived layer;
/// anything unconvertible stays in place with a diagnostic and is excluded from plots.
/// </summary>
public static class UnitSystem
{
    // Canonical per-axis units for this offline case domain.
    public static readonly Dictionary<string, string> CanonicalUnit = new()
    {
        ["d13C"] = "permil_VPDB",
        ["d15N"] = "permil_AIR",
        ["d18O"] = "permil_VSMOW",
        ["d34S"] = "permil_VCDT",
        ["d2H"] = "permil_VSMOW"
    };

    public static double? Convert(string axis, string? fromUnit, double value, out string? diagnostic)
    {
        diagnostic = null;
        string canonical = CanonicalUnit.TryGetValue(axis, out var c) ? c : "permil";
        if (string.IsNullOrWhiteSpace(fromUnit) || fromUnit == canonical) return value;

        return fromUnit switch
        {
            // Same isotope scale expressed in fractional rather than per-mil notation.
            "fraction_VPDB" when axis == "d13C" => value * 1000,
            "fraction_AIR" when axis == "d15N" => value * 1000,
            "fraction_VSMOW" when axis is "d18O" or "d2H" => value * 1000,
            "fraction_VCDT" when axis == "d34S" => value * 1000,
            _ => Unconvertible(axis, fromUnit, value, out diagnostic)
        };
    }

    private static double? Unconvertible(string axis, string fromUnit, double value, out string? diagnostic)
    {
        diagnostic = $"UNCONVERTIBLE_UNIT: axis={axis} unit={fromUnit} value={value}; kept in original location, excluded from normalization and plotting.";
        return null;
    }
}

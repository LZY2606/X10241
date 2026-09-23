namespace IsoMix.Engine;

/// <summary>
/// Unit / coordinate-basis handling. Raw values are never rewritten at import;
/// normalization happens only here, in the derived layer. Values that cannot be
/// converted stay in place and produce a diagnostic.
/// </summary>
public static class Units
{
    public const string CanonicalUnit = "permil";

    /// <summary>Standard ratios for delta-scale bases (isotope ratio of the reference material).</summary>
    public static readonly IReadOnlyDictionary<string, double> BasisStandardRatio =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["VPDB"] = 0.0111802,     // 13C/12C
            ["AIR"] = 0.0036765,      // 15N/14N
            ["AIR_N2"] = 0.0036765,
            ["VSMOW"] = 0.0020052,    // 18O/16O
            ["VCDT"] = 0.000450045,   // 34S/32S
            ["CDT"] = 0.000450045,
        };

    /// <summary>
    /// Convert a raw quantity into canonical per-mil. Returns false when the unit or
    /// basis is unknown; the caller must keep the raw value and emit a diagnostic.
    /// </summary>
    public static bool TryNormalize(double rawValue, string? unit, string? basis, out double canonical, out string? diagnostic)
    {
        canonical = rawValue;
        diagnostic = null;
        var u = (unit ?? CanonicalUnit).Trim();
        if (u.Equals("permil", StringComparison.OrdinalIgnoreCase) || u == "‰")
        {
            if (string.IsNullOrWhiteSpace(basis))
            {
                diagnostic = $"basis missing for per-mil value {rawValue}; assumed unconvertible until basis is declared";
                return false;
            }
            if (!BasisStandardRatio.ContainsKey(basis))
            {
                diagnostic = $"unknown coordinate basis '{basis}'";
                return false;
            }
            return true;
        }
        if (u.Equals("ratio", StringComparison.OrdinalIgnoreCase) || u.Equals("atom_ratio", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(basis) || !BasisStandardRatio.TryGetValue(basis, out var std))
            {
                diagnostic = $"cannot convert isotope ratio to per-mil without a known basis (got '{basis ?? "none"}')";
                return false;
            }
            canonical = (rawValue / std - 1.0) * 1000.0;
            return true;
        }
        diagnostic = $"unknown unit '{u}'";
        return false;
    }
}

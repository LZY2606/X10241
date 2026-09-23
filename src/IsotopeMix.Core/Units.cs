namespace IsotopeMix.Core;

/// <summary>A domain quantity that always carries its unit and coordinate basis.</summary>
public sealed record Quantity(double Value, string Unit, string Basis)
{
    public string StableRef => $"{Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} {Unit} [{Basis}]";
}

public sealed record UnitConversion(Quantity Converted, string Rule);

/// <summary>
/// Unit conversion registry. Raw values are never rewritten in place; conversion
/// happens only in the derived layer. Unconvertible values return null and the
/// caller must keep the raw value with a diagnostic.
/// </summary>
public static class UnitConverter
{
    // Standard ratios for delta <-> ratio conversion, keyed by basis.
    private static readonly IReadOnlyDictionary<string, double> StandardRatios =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["V-PDB"] = 0.0112372,   // 13C/12C
            ["AIR"] = 0.0036765,     // 15N/14N
            ["V-CDT"] = 0.0441626,   // 34S/32S
        };

    public static bool CanConvert(string fromUnit, string toUnit, string basis) =>
        TryConvert(new Quantity(1.0, fromUnit, basis), toUnit) is not null;

    public static UnitConversion? TryConvert(Quantity q, string targetUnit)
    {
        if (string.Equals(q.Unit, targetUnit, StringComparison.OrdinalIgnoreCase))
            return new UnitConversion(q, "identity");
        if (q.Unit.Equals("permil", StringComparison.OrdinalIgnoreCase) &&
            targetUnit.Equals("ratio", StringComparison.OrdinalIgnoreCase))
        {
            if (!StandardRatios.TryGetValue(q.Basis, out var rstd)) return null;
            return new UnitConversion(
                new Quantity((q.Value / 1000.0 + 1.0) * rstd, "ratio", q.Basis),
                $"ratio=(delta/1000+1)*Rstd({q.Basis})");
        }
        if (q.Unit.Equals("ratio", StringComparison.OrdinalIgnoreCase) &&
            targetUnit.Equals("permil", StringComparison.OrdinalIgnoreCase))
        {
            if (!StandardRatios.TryGetValue(q.Basis, out var rstd)) return null;
            return new UnitConversion(
                new Quantity((q.Value / rstd - 1.0) * 1000.0, "permil", q.Basis),
                $"delta=(ratio/Rstd({q.Basis})-1)*1000");
        }
        return null;
    }
}

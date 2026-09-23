namespace IsoMix.Domain;

/// <summary>One isotope dimension declared with its unit and coordinate basis (reference standard).</summary>
public record UnitsDim(string Name, string Unit, string Basis, double StandardRatio);

/// <summary>
/// Unit registry for the derived layer. Raw records keep their original representation;
/// conversion to the canonical per-mil scale happens only here. Values whose unit cannot
/// be converted stay in place and surface a diagnostic instead of being silently dropped.
/// </summary>
public static class Units
{
    public const string CanonicalUnit = "permil";

    /// <summary>Attempts to convert <paramref name="value"/> expressed in <paramref name="unit"/> to per-mil.</summary>
    public static bool TryToPermil(string unit, double standardRatio, double value, out double permil)
    {
        switch (unit)
        {
            case "permil":
            case "‰":
                permil = value;
                return true;
            case "ratio":
                permil = (value / standardRatio - 1.0) * 1000.0;
                return true;
            case "percent":
                permil = value * 10.0;
                return true;
            default:
                permil = value;
                return false;
        }
    }

    public static double PermilToRatio(double permil, double standardRatio) =>
        standardRatio * (1.0 + permil / 1000.0);
}

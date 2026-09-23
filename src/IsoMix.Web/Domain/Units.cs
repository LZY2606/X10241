namespace IsoMix.Domain;

// A domain quantity always carries its raw representation plus unit/datum.
// Normalization happens only in the derived layer; unconvertible values stay
// in place with a diagnostic attached.
public sealed record Quantity(
    string Raw,
    string Unit,
    string Datum,
    double? Normalized,
    string? Diagnostic)
{
    public static Quantity Of(string raw, string unit, string datum)
    {
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return new Quantity(raw, unit, datum, null,
                $"value '{raw}' is not numeric; kept in raw form");
        }
        double factor = unit switch
        {
            "permille" or "‰" or "1" => 1.0,
            "percent" or "%" => 10.0,       // 1 percent = 10 permille
            "fraction" => 1000.0,           // 1 fraction = 1000 permille
            _ => double.NaN
        };
        if (double.IsNaN(factor))
        {
            return new Quantity(raw, unit, datum, null,
                $"unit '{unit}' has no conversion to permille; kept in raw form");
        }
        return new Quantity(raw, unit, datum, value * factor, null);
    }
}

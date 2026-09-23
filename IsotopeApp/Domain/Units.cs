namespace IsotopeApp.Domain;

/// <summary>
/// A measurement unit or coordinate reference (e.g. "permil VPDB", "permil AIR",
/// "mg/L"). The <see cref="RawToken"/> preserves exactly how the value appeared
/// in the imported file; nothing is normalised at import time.
/// </summary>
public sealed record MeasureUnit(string Code, string RawToken)
{
    public static MeasureUnit Parse(string? text)
    {
        var t = (text ?? string.Empty).Trim();
        var code = t.ToLowerInvariant() switch
        {
            "" => "unknown",
            "permil" or "‰" or "ppt" or "parts-per-thousand" => "permil",
            "permil vpdb" or "‰ vpdb" or "vpdb" => "permil VPDB",
            "permil air" or "‰ air" or "air" => "permil AIR",
            "permil vcdt" or "vcdt" => "permil VCDT",
            "mg/l" => "mg/L",
            "ug/l" => "ug/L",
            _ => t
        };
        return new MeasureUnit(code, t.Length == 0 ? "(blank)" : t);
    }
}

/// <summary>
/// Declares whether two units are inter-convertible. Isotope delta scales with
/// different reference standards (VPDB vs VCDT etc.) cannot be mixed without an
/// explicit conversion; such values stay in place with a diagnostic instead of
/// being silently dropped for the plot.
/// </summary>
public static class UnitConversions
{
    public static bool CanConvert(MeasureUnit from, MeasureUnit to)
    {
        if (from.Code == to.Code) return true;
        if (from.Code == "unknown" || to.Code == "unknown") return false;
        return (from.Code, to.Code) switch
        {
            ("permil VPDB", "permil") or ("permil", "permil VPDB") => true,
            ("permil AIR", "permil") or ("permil", "permil AIR") => true,
            ("permil VCDT", "permil") or ("permil", "permil VCDT") => true,
            _ => false
        };
    }
}

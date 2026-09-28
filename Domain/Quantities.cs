using System;
using System.Collections.Generic;
using System.Globalization;

namespace IsotopeMixing.Domain;

public readonly record struct Converted(double? Value, bool ConvertedOk, string Diagnostic, string CanonicalUnit);

public static class Quantities
{
    public static Converted Convert(ValueRep rep, string expectedUnit)
    {
        if (rep == null || string.IsNullOrWhiteSpace(rep.Raw))
            return new(null, false, "missing-raw-value", expectedUnit);
        if (!double.TryParse(rep.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return new(null, false, $"unparseable-numeric:{rep.Raw}", expectedUnit);

        var unit = (rep.Unit ?? expectedUnit ?? "").Trim().Replace("‰", "%");
        var expected = (expectedUnit ?? "").Trim().Replace("‰", "%");
        return unit switch
        {
            "%" or "permil" when expected == "%" || expected == "permil" || expected == "" => new(v, true, "", "%"),
            "fraction" when expected == "%" || expected == "permil" || expected == "" => new(v * 1000.0, true, "converted:fraction->permil", "%"),
            "%" or "permil" when unit == expected => new(v, true, "", expected),
            _ when expected != "" && unit == expected => new(v, true, "", expected),
            _ => new(null, false, $"incompatible-or-unknown-unit:{unit}", expected)
        };
    }

    public static double ConvertOrNaN(ValueRep rep, string expectedUnit, out string diagnostic)
    {
        var c = Convert(rep, expectedUnit);
        diagnostic = c.Diagnostic;
        return c.Value ?? double.NaN;
    }
}

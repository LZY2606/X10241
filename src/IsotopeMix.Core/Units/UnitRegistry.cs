using System;
using System.Collections.Generic;

namespace IsotopeMix.Core.Units;

public sealed record UnitDiagnostic(string Code, string Message);

/// <summary>
/// Registry of isotopic coordinate bases. Normalisation is performed only in the derived layer;
/// the raw text/basis of every imported quantity is retained unchanged by the storage layer.
/// </summary>
public static class UnitRegistry
{
    // quantity -> basis -> (scale, offset) mapping to the canonical basis.
    private static readonly Dictionary<string, string> CanonicalBasis = new()
    {
        ["d13C"] = "VPDB",
        ["d18O"] = "VSMOW",
        ["d15N"] = "AIR",
        ["d2H"] = "VSMOW",
        ["d34S"] = "VCDT",
    };

    private static readonly Dictionary<(string, string), (double Scale, double Offset)> ToCanonical = new()
    {
        // Coplen (1988) conversions for oxygen isotope delta scales.
        [("d18O", "VPDB")] = (1.03091, 30.91),
        [("d18O", "VSMOW")] = (1.0, 0.0),
        [("d13C", "VPDB")] = (1.0, 0.0),
        [("d15N", "AIR")] = (1.0, 0.0),
        [("d2H", "VSMOW")] = (1.0, 0.0),
        [("d34S", "VCDT")] = (1.0, 0.0),
    };

    public static string? Canonical(string quantity) =>
        CanonicalBasis.TryGetValue(quantity, out var b) ? b : null;

    public static bool IsKnownBasis(string quantity, string basis) =>
        ToCanonical.ContainsKey((quantity, basis));

    /// <summary>Convert a value expressed in <paramref name="basis"/> into the canonical basis.</summary>
    public static (double? value, UnitDiagnostic? diagnostic) Normalize(
        string quantity, string basis, double value)
    {
        if (!CanonicalBasis.ContainsKey(quantity))
        {
            return (null, new UnitDiagnostic("UNIT_UNKNOWN_QUANTITY",
                $"Quantity '{quantity}' has no registered canonical basis; value retained at its original position."));
        }
        if (!ToCanonical.TryGetValue((quantity, basis), out var conv))
        {
            return (null, new UnitDiagnostic("UNIT_UNCONVERTED",
                $"Cannot convert '{quantity}' from basis '{basis}' to '{CanonicalBasis[quantity]}'; value retained for inspection."));
        }
        return (conv.Scale * value + conv.Offset, null);
    }
}

/// <summary>An isotope quantity exactly as recorded, plus optional derived normalisation.</summary>
public sealed class IsotopicQuantity
{
    public required string Dimension { get; init; }
    public required string RawText { get; init; }
    public required string RawBasis { get; init; }
    public double? ParsedValue { get; init; }
    public double? NormalizedValue { get; init; }
    public string? CanonicalBasis { get; init; }
    public UnitDiagnostic? Diagnostic { get; init; }

    public bool UsableForInference => ParsedValue is not null && NormalizedValue is not null && Diagnostic is null;

    public static IsotopicQuantity Parse(string dimension, string rawText, string rawBasis)
    {
        double? parsed = double.TryParse(rawText,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
        double? normalized = null;
        UnitDiagnostic? diagnostic = null;
        var canonical = UnitRegistry.Canonical(dimension);
        if (parsed is not null)
        {
            var (n, d) = UnitRegistry.Normalize(dimension, rawBasis, parsed.Value);
            normalized = n;
            diagnostic = d;
        }
        else
        {
            diagnostic = new UnitDiagnostic("VALUE_UNPARSEABLE",
                $"Raw text '{rawText}' could not be parsed; retained in place, excluded from derived calculations.");
        }
        return new IsotopicQuantity
        {
            Dimension = dimension,
            RawText = rawText,
            RawBasis = rawBasis,
            ParsedValue = parsed,
            NormalizedValue = normalized,
            CanonicalBasis = canonical,
            Diagnostic = diagnostic,
        };
    }
}

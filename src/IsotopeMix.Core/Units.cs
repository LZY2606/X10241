namespace IsotopeMix.Core;

/// <summary>
/// Unit / coordinate-basis handling. Import keeps the raw representation; only the
/// derived layer normalizes. Values that cannot be converted stay in place and get
/// a diagnostic instead of being silently dropped.
/// </summary>
public static class Units
{
    private static readonly Dictionary<string, double> ReferenceRatios = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VPDB"] = 0.0111802,
        ["AIR"] = 0.00366204,
        ["VCDT"] = 0.0441626,
    };

    public static bool IsKnown(string unit, string standard) =>
        unit.Equals("permil", StringComparison.OrdinalIgnoreCase) && ReferenceRatios.ContainsKey(standard);

    /// <summary>Convert a permil value to isotope ratio. Returns null when not convertible.</summary>
    public static double? PermilToRatio(IsotopeValue v)
    {
        if (!v.Unit.Equals("permil", StringComparison.OrdinalIgnoreCase)) return null;
        if (!ReferenceRatios.TryGetValue(v.Standard, out var r)) return null;
        return (v.Value / 1000.0 + 1.0) * r;
    }

    /// <summary>
    /// Normalize a set of values into a common axis order. Unconvertible values remain
    /// in <paramref name="keptRaw"/> with a diagnostic; they are never dropped silently.
    /// </summary>
    public static bool TryNormalize(
        IReadOnlyList<IsotopeValue> values,
        IReadOnlyList<string> axisOrder,
        out double[] normalized,
        out List<JudgementEvent> diagnostics,
        out List<IsotopeValue> keptRaw)
    {
        normalized = new double[axisOrder.Count];
        diagnostics = new();
        keptRaw = new();
        var ok = true;
        for (var i = 0; i < axisOrder.Count; i++)
        {
            var v = values.FirstOrDefault(x => x.AxisId == axisOrder[i]);
            if (v is null)
            {
                ok = false;
                diagnostics.Add(new JudgementEvent("MissingIsotopeDimension",
                    $"Axis '{axisOrder[i]}' has no measured value; raw record kept.",
                    new Dictionary<string, string> { ["axis"] = axisOrder[i] }));
                continue;
            }
            if (!IsKnown(v.Unit, v.Standard))
            {
                ok = false;
                keptRaw.Add(v);
                diagnostics.Add(new JudgementEvent("UnconvertibleUnit",
                    $"Value for axis '{v.AxisId}' has unknown unit/standard '{v.Unit}/{v.Standard}'; left in place with diagnostic.",
                    new Dictionary<string, string> { ["axis"] = v.AxisId, ["unit"] = v.Unit, ["standard"] = v.Standard }));
                continue;
            }
            normalized[i] = v.Value; // derived layer works in permil on the declared standard
        }
        return ok;
    }
}

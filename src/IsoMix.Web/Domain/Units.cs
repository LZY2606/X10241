namespace IsoMix.Domain;

public sealed record UnitConversion(double Factor, string Canonical, string Family);

public static class UnitRegistry
{
    private static readonly Dictionary<string, UnitConversion> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["permille"] = new(1.0, "permille", "delta"),
        ["permil"] = new(1.0, "permille", "delta"),
        ["per mil"] = new(1.0, "permille", "delta"),
        ["‰"] = new(1.0, "permille", "delta"),
        ["vpdb"] = new(1.0, "VPDB", "delta-VPDB"),
        ["δ-vpdb"] = new(1.0, "VPDB", "delta-VPDB"),
        ["vsmow"] = new(1.0, "VSMOW", "delta-VSMOW"),
        ["δ-vsmow"] = new(1.0, "VSMOW", "delta-VSMOW"),
        ["air"] = new(1.0, "AIR", "delta-AIR"),
        ["δ-air"] = new(1.0, "AIR", "delta-AIR"),
    };

    public static bool IsRecognized(string? unit) =>
        unit is not null && Map.ContainsKey(unit.Trim());

    public static bool TryResolve(string? unit, out UnitConversion conversion)
    {
        conversion = default!;
        return unit is not null && Map.TryGetValue(unit.Trim(), out conversion!);
    }
}

using System.Globalization;

namespace IsotopeMix.Domain;

/// <summary>
/// 领域量永远携带单位/坐标基准。原始表示在 Raw 中原样保留，
/// 派生层才做规范化；无法转换的量保留在原处并附诊断。
/// </summary>
public enum UnitKind
{
    DeltaPermil,        // δ (per mil, ‰)，线性混合空间中的常规表示
    DeltaFraction,      // δ 以小数表示（如 -0.000264），= ‰ / 1000
    RatioR,             // 同位素比值 R（非线性，不直接参与线性混合）
    Unknown
}

public sealed record DimValue(double? Value, string RawText, UnitKind Unit, string Scale, string? Diagnostic = null)
{
    public bool Convertible => Value.HasValue && Unit is UnitKind.DeltaPermil or UnitKind.DeltaFraction;
    public double? ToPermil()
    {
        if (!Value.HasValue) return null;
        return Unit switch
        {
            UnitKind.DeltaPermil => Value.Value,
            UnitKind.DeltaFraction => Value.Value * 1000.0,
            _ => null
        };
    }
}

public static class Units
{
    public static readonly string[] ScaleAliasesD13C = { "VPDB", "PDB", "δ13C VPDB", "d13C VPDB" };
    public static readonly string[] ScaleAliasesD15N = { "AIR", "δ15N AIR", "d15N AIR" };

    public static DimValue Parse(string raw, string scaleHint)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals("NA", StringComparison.OrdinalIgnoreCase)
            || text.Equals("missing", StringComparison.OrdinalIgnoreCase)
            || text.Equals("n/a", StringComparison.OrdinalIgnoreCase))
            return new DimValue(null, raw ?? "", UnitKind.Unknown, scaleHint, "missing-value");

        // 保留原始文本；只在规范化时解析
        var normalized = text.Replace("‰", "").Replace("%", "").Replace(" ", "");
        UnitKind kind;
        if (text.Contains('R') && !text.Contains('δ') && !text.Contains('d'))
            kind = UnitKind.RatioR;
        else if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            // δ 的小数形式：|v| < 0.1 且原始写法带小数点很多位，判定为 fraction；否则 per mil
            kind = Math.Abs(v) < 0.1 && normalized.Contains('.') && (normalized.Length - normalized.IndexOf('.')) > 3
                ? UnitKind.DeltaFraction
                : (text.Contains('‰') || !normalized.Contains('.') || Math.Abs(v) >= 0.1
                    ? UnitKind.DeltaPermil
                    : UnitKind.DeltaFraction);
        }
        else
            return new DimValue(null, raw ?? "", UnitKind.Unknown, scaleHint, "unparseable");

        return new DimValue(v, raw ?? "", kind, NormalizeScale(scaleHint));
    }

    public static string NormalizeScale(string scale) => (scale ?? "").Trim().ToUpperInvariant() switch
    {
        "PDB" => "VPDB",
        "" => "UNKNOWN",
        var s => s
    };

    public static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

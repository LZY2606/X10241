namespace IsotopeMix.Core.Units;

/// <summary>
/// 领域单位/坐标基准。稳定同位素以 ‰ (permil) 相对标准报告，
/// 派生层在规范化时统一换算；原始表示始终保留在 RawRecord 上。
/// </summary>
public enum UnitKind
{
    Permil,        // δ 值，相对 VPDB/VSMOW 等标准的千分比
    Percent,       // 百分比形态（部分实验室原样录入）
    Ratio,         // 小数比率
    Concentration, // 浓度（需要尺度参数）
    Dimensionless
}

public sealed record UnitDef(
    UnitKind Kind,
    string Reference,   // 例：VPDB、AIR、VSMOW
    string OriginalText,
    double? Scale = null)
{
    public static readonly UnitDef PermilVpdb = new(UnitKind.Permil, "VPDB", "permil vs VPDB");
    public static readonly UnitDef PermilAir = new(UnitKind.Permil, "AIR", "permil vs AIR");

    /// <summary>
    /// 将原始数值规范化到 ‰ 基准。无法转换时返回 null 并保留诊断。
    /// 绝不删除原始值。
    /// </summary>
    public double? ToPermil(double raw, out string? diagnostic)
    {
        diagnostic = null;
        return Kind switch
        {
            UnitKind.Permil => raw,
            UnitKind.Percent => raw * 10.0,
            UnitKind.Ratio => (raw - 1.0) * 1000.0,
            UnitKind.Concentration => Cannot(raw, out diagnostic),
            UnitKind.Dimensionless when Scale is { } s => raw * s,
            UnitKind.Dimensionless => raw,
            _ => Cannot(raw, out diagnostic)
        };
    }

    private static double? Cannot(double raw, out string? diagnostic)
    {
        diagnostic = $"无法把单位转换到 ‰ 基准：原始值 {raw} 保留在原位置";
        return null;
    }
}

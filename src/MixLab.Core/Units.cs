namespace MixLab.Core;

/// <summary>
/// 派生层的单位规范化。原始记录在导入时保持原样；只有进入计算前才转换。
/// 无法转换的值返回 null，由调用方留在原位置并附诊断，绝不静默丢弃或猜测。
/// </summary>
public static class Units
{
    public const string Permil = "permil";
    public const string Fraction = "fraction";

    public static bool IsPermil(string unit) =>
        unit is "permil" or "‰" or "per-mil" or "per_mil";

    public static bool IsFraction(string unit) =>
        unit is "fraction" or "1" or "dimensionless";

    /// <summary>转换到 canonical 单位 permil；无法转换返回 null。</summary>
    public static double? ToPermil(double value, string unit)
    {
        if (IsPermil(unit)) return value;
        if (IsFraction(unit)) return value * 1000.0;
        return null;
    }

    /// <summary>协方差/方差单位转换到 permil^2；无法转换返回 null。</summary>
    public static double? VarianceToPermil2(double value, string unit)
    {
        if (unit is "permil^2" or "permil2" or "‰^2") return value;
        if (IsPermil(unit)) return value; // 已按方差给出的 permil 值
        if (unit is "fraction^2" or "fraction2") return value * 1_000_000.0;
        return null;
    }
}

using System.Text.Json.Serialization;

namespace IsoMix.Domain;

/// <summary>同位素维（示踪剂坐标），例如 d13C / d15N / d18O。</summary>
public sealed record IsotopeAxis(string Key, string Label, string Unit, string? Datum = null);

/// <summary>
/// 带单位/坐标基准的量值。RawText 保留导入时的原始表示（原样、不做不可逆清洗），
/// 派生层才使用 Normalized 数值。无法转换时 RawText 留在原位、Parsed=false 并附诊断。
/// </summary>
public sealed record MeasuredValue
{
    public string RawText { get; init; } = "";
    public double? Value { get; init; }
    public string? Unit { get; init; }
    public string? Datum { get; init; }
    public bool Parsed { get; init; } = true;
    public string? ParseDiagnostic { get; init; }

    public static MeasuredValue Unparseable(string raw, string? unit, string diagnostic) =>
        new() { RawText = raw, Value = null, Unit = unit, Parsed = false, ParseDiagnostic = diagnostic };
}

/// <summary>样品：在若干同位素维上的观测值。缺失维通过 MissingAxes 显式记录，而非静默补零。</summary>
public sealed class Sample
{
    public string StableId { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, MeasuredValue> Values { get; set; } = new();
    public List<string> MissingAxes { get; set; } = new();
    /// <summary>测量协方差记录的稳定 id（相关误差按此传播）。</summary>
    public string? CovarianceId { get; set; }
    public string? Note { get; set; }
}

/// <summary>端元（源）分布。坐标为各同位素维的端元组成；Uncertainty 为端元自身不确定度。</summary>
public sealed class Endmember
{
    public string StableId { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, MeasuredValue> Composition { get; set; } = new();
    /// <summary>各维标准差（可选），用于端元不确定度。</summary>
    public Dictionary<string, double> StdDev { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>端元集合的一个版本。</summary>
public sealed class EndmemberSet
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public List<Endmember> Members { get; set; } = new();
}

/// <summary>
/// 测量协方差矩阵记录。Matrix 为逐行（按 AxisOrder）的方差/协方差；
/// 非对角元素非零即相关误差。原始文本矩阵同样保留在 RawText。
/// </summary>
public sealed class MeasurementCovariance
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> AxisOrder { get; set; } = new();
    public double[,] Matrix { get; set; } = { };
    public string? RawText { get; set; }
    public string? Unit { get; set; }
}

/// <summary>分馏因子：对每个端元（或全部）在每个同位素维上的位移 Δ 与单位。</summary>
public sealed class FractionationFactor
{
    public string Axis { get; set; } = "";
    /// <summary>null 表示对所有端元统一应用；否则为具体端元 stable id。</summary>
    public string? EndmemberId { get; set; }
    public double Delta { get; set; }
    public string Unit { get; set; } = "";
}

/// <summary>分馏假设的一个版本。</summary>
public sealed class FractionationVersion
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public List<FractionationFactor> Factors { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>
/// 先验约束。Bounds 给出每个端元比例的闭区间 [Lo,Hi]；
/// 与混合单纯形（和为一、非负）相交后可能为空——空集即“不可行先验”。
/// 绝不通过“截断负值后归一化”来伪造可行性。
/// </summary>
public sealed class PriorAssumption
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, double> Lo { get; set; } = new();
    public Dictionary<string, double> Hi { get; set; } = new();
    public string? Note { get; set; }
}

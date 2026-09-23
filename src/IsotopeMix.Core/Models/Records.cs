using System.Text.Json.Serialization;

namespace IsotopeMix.Core.Models;

// 材料角色：样品 / 端元（源组成室）
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MaterialRole
{
    Sample = 0,
    Endmember = 1
}

// 一个同位素测量维的“原始表示”。数值与其单位/基准永不分离。
public sealed class IsotopeValue
{
    public string Element { get; set; } = "";      // 例如 C / N / O / H
    public string Tracer { get; set; } = "";       // 例如 d13C / d15N
    public string RawText { get; set; } = "";      // 导入时的原始字符串表示
    public double? Value { get; set; }             // 解析后的数值；无法转换时为 null
    public string Unit { get; set; } = "";         // 原始单位，例如 permil-VPDB / ppm
    public string ReferenceFrame { get; set; } = ""; // 坐标/同位素基准，例如 VPDB / AIR
}

// 材料（样品或端元）。来源层记录：不做任何不可逆清洗。
public sealed class MaterialRecord
{
    public string Id { get; set; } = "";           // 稳定身份，例如 M-S1 / M-E1
    public MaterialRole Role { get; set; }
    public string Name { get; set; } = "";
    public string Matrix { get; set; } = "";       // 载体，例如 POM / 土壤有机碳
    public List<IsotopeValue> Values { get; set; } = new();
    public string? CovarianceId { get; set; }      // 端元分布或样品测量协方差
    public string Notes { get; set; } = "";
    public string SourceEventId { get; set; } = "";
}

// 测量/端元协方差：对称正定（或半正定）矩阵，维度顺序随 Tracers。
public sealed class CovarianceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Tracers { get; set; } = new();  // 与矩阵行列对应的维
    public List<string> Units { get; set; } = new();    // 每个维的单位
    public double[,] Matrix { get; set; } = new double[0, 0];
    // 标记误差是否相关（非对角项非零），供“相关误差按协方差传播”事件断言。
    [JsonIgnore]
    public bool IsCorrelated
    {
        get
        {
            for (int r = 0; r < Matrix.GetLength(0); r++)
                for (int c = r + 1; c < Matrix.GetLength(1); c++)
                    if (Matrix[r, c] != 0.0) return true;
            return false;
        }
    }
    public string SourceEventId { get; set; } = "";
}

// 分馏版本：对每个示踪物给出端元→混合物的分馏校正项（原始单位）。
// 同一材料集合可挂多个分馏版本，复核界面并排比较。
public sealed class FractionationVersion
{
    public string Id { get; set; } = "";           // 例如 F-V0 / F-V1
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";         // equilibrium / kinetic / none
    public string Description { get; set; } = "";
    // tracer -> 每个端元的分馏因子（键为端元 Id）；0 表示无分馏。
    public Dictionary<string, Dictionary<string, double>> OffsetByTracerEndmember { get; set; } = new();
    public string Unit { get; set; } = "permil";
    public int Revision { get; set; } = 1;
    public string SupersedesFractionationId { get; set; } = ""; // 旧版本仍保留
    public string SourceEventId { get; set; } = "";
}

// 先验假设：对比例的先验限制（箱型/方向），以及全局是否强制 simplex。
public sealed class PriorAssumption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // 按端元 Id 给出的比例上下界（0..1）；缺省为 [0,1]。
    public Dictionary<string, double> LowerByEndmember { get; set; } = new();
    public Dictionary<string, double> UpperByEndmember { get; set; } = new();
    public bool EnforceSimplex { get; set; } = true; // 和为一且非负
    public string Rationale { get; set; } = "";
    public string SourceEventId { get; set; } = "";
}

// 单位登记：声明单位族、基准与到内部规范化单位的仿射变换（offset/scale）。
// 派生层才做规范化；来源层保持原值原单位。
public sealed class UnitDefinition
{
    public string Unit { get; set; } = "";
    public string QuantityKind { get; set; } = "";   // delta / ratio / concentration
    public string ReferenceFrame { get; set; } = "";
    public string CanonicalUnit { get; set; } = "";
    public double ToCanonicalScale { get; set; } = 1.0;
    public double ToCanonicalOffset { get; set; } = 0.0;
}

// 无法转换/无法规范化的派生层诊断。原值仍留在原位置，绝不丢弃。
public sealed class ConversionDiagnostic
{
    public string RecordId { get; set; } = "";
    public string Tracer { get; set; } = "";
    public string RawText { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Problem { get; set; } = "";
}

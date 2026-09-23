using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Models;

/// <summary>原始导入文件：字节逐字保存，禁止在导入时清洗。</summary>
public class RawDocument
{
    public string Id { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string MediaType { get; set; } = "application/json";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string Sha256 { get; set; } = "";
    public string ImportedAt { get; set; } = "";
}

/// <summary>一条领域记录的某一版本。单位/坐标基准与原始表示同时保留。</summary>
public class RecordVersion
{
    public string RecordId { get; set; } = "";
    public string Kind { get; set; } = "";          // sample|endmember|covariance|fractionation|prior
    public string Version { get; set; } = "";       // 版本指纹（sha256 短码）
    public string SourceDocId { get; set; } = "";
    public string SourceFragmentJson { get; set; } = "";  // 输入片段（原始）
    public string PayloadJson { get; set; } = "";  // 结构化内容（派生层规范化）
    public string CreatedAt { get; set; } = "";
    public List<UnitDiagnostic> Diagnostics { get; set; } = new();
}

public class UnitDiagnostic
{
    public string Severity { get; set; } = "warning"; // error|warning
    public string Field { get; set; } = "";
    public string Message { get; set; } = "";
}

// ---------- 领域载荷（全部携带单位/坐标基准） ----------

public class SamplePayload
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<IsotopeObservation> Observations { get; set; } = new();
}

public class IsotopeObservation
{
    public string Tracer { get; set; } = "";        // 如 d13C, d15N, Sr87/86
    public double? Value { get; set; }
    public string Unit { get; set; } = "";          // permil_VPDB / ratio 等
    public string RawText { get; set; } = "";       // 原始表示
    public bool Missing => Value is null;
}

public class EndmemberPayload
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>每个端元有自己的分布：均值 + 协方差 + 支撑（多候选/离散支撑）。</summary>
    public List<IsotopeObservation> Mean { get; set; } = new();
    /// <summary>端元分布的协方差（同 tracer 序），可空。</summary>
    public List<List<double>>? Covariance { get; set; }
    /// <summary>离散支撑候选点（用于“多个可行区”先验），可空。</summary>
    public List<List<double?>>? Support { get; set; }
}

public class CovariancePayload
{
    public string SampleCode { get; set; } = "";
    public List<string> Tracers { get; set; } = new();
    /// <summary>行优先协方差矩阵；允许相关误差（非对角项）。</summary>
    public List<List<double>> Matrix { get; set; } = new();
}

public class FractionationPayload
{
    public string VersionTag { get; set; } = "";    // 分馏版本身份
    public string Name { get; set; } = "";
    /// <summary>tracer -> 每个端元的分馏校正量（与端元 code 对齐），单位随 tracer。</summary>
    public Dictionary<string, Dictionary<string, double>> Shifts { get; set; } = new();
    public string Note { get; set; } = "";
}

public class PriorPayload
{
    public string SampleCode { get; set; } = "";
    /// <summary>下界（simplex 约束之外的先验），按端元 code。</summary>
    public Dictionary<string, double>? Lower { get; set; }
    /// <summary>上界，按端元 code。</summary>
    public Dictionary<string, double>? Upper { get; set; }
    /// <summary>允许的端元基数（最多使用几个端元）；null 表示不限。</summary>
    public int? MaxSources { get; set; }
}

public static class Json
{
    public static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Ser(object o) => JsonSerializer.Serialize(o, Opt);
    public static T? Des<T>(string s) => JsonSerializer.Deserialize<T>(s, Opt);
}

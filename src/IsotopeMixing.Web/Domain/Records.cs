using System.Text.Json.Serialization;

namespace IsotopeMixing.Web.Domain;

// 同位素向量：每个同位素维保留原始表示 + 解析后的数值（无法转换时 null）。
public sealed record IsoValue(
    [property: JsonPropertyName("raw")] string Raw,
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("diagnostic")] string? Diagnostic = null);

public sealed record Sample(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("values")] Dictionary<string, IsoValue> Values);

public sealed record EndmemberVersion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("familyId")] string FamilyId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("means")] Dictionary<string, IsoValue> Means,
    [property: JsonPropertyName("sourceSha")] string SourceSha);

public sealed record CovarianceSpec(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sampleId")] string SampleId,
    // 全协方差（允许非对角项，检验相关误差传播）。矩阵按 dimensions 顺序索引。
    [property: JsonPropertyName("dimensions")] List<string> Dimensions,
    [property: JsonPropertyName("matrix")] List<List<double>> Matrix,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("correlated")] bool Correlated);

public sealed record FractionationPath(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("shifts")] Dictionary<string, double> Shifts);

public sealed record FractionationVersion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    // additive=true: x_eff = x_sample - shift；端元在模型中为 (mean + shift)。
    [property: JsonPropertyName("additive")] bool Additive,
    [property: JsonPropertyName("paths")] List<FractionationPath> Paths);

public sealed record PriorSpec(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    // 按端元版本 id 给出的硬下界；sumLower>1 等情形即“不可行先验”。
    [property: JsonPropertyName("lowerBounds")] Dictionary<string, double> LowerBounds,
    [property: JsonPropertyName("rationale")] string Rationale);

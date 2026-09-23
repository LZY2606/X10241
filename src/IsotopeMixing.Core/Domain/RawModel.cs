using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Domain;

/// <summary>
/// 原始记录信封: 来源与解释分开保存的“来源”侧。
/// Content 保留逐字节导入的 JSON 表示, 导入时不做任何不可逆清洗。
/// </summary>
public sealed record RawRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,           // sample|endmember|covariance|fractionation|prior|dimension
    [property: JsonPropertyName("materialKey")] string MaterialKey,
    [property: JsonPropertyName("sourceRef")] SourceRef Source,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("receivedAt")] string ReceivedAt,
    [property: JsonPropertyName("content")] JsonElement Content,
    [property: JsonPropertyName("versionTag")] string VersionTag = "v1",
    [property: JsonPropertyName("supersedes")] string? Supersedes = null);

public sealed record SourceRef(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("row")] string? Row = null,
    [property: JsonPropertyName("page")] string? Page = null,
    [property: JsonPropertyName("mediaType")] string? MediaType = null);

/// <summary>样品原始记录(内容结构; 未识别字段仍保留在 RawRecord.Content 中)。</summary>
public sealed record SampleContent(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("site")] string? Site,
    [property: JsonPropertyName("coordinate")] Coordinate? Coordinate,
    [property: JsonPropertyName("values")] Dictionary<string, Quantity> Values,
    [property: JsonPropertyName("covarianceRef")] string? CovarianceRef,
    [property: JsonPropertyName("notes")] string? Notes = null);

public sealed record Coordinate(
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lon")] double Lon,
    [property: JsonPropertyName("datum")] string Datum = "WGS84");

/// <summary>端元原始记录; 多模态分布用 Modes 显式给出, 可产生多个可行区。</summary>
public sealed record EndmemberContent(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("modes")] List<EndmemberMode> Modes,
    [property: JsonPropertyName("covarianceRef")] string? CovarianceRef = null,
    [property: JsonPropertyName("notes")] string? Notes = null);

public sealed record EndmemberMode(
    [property: JsonPropertyName("modeId")] string ModeId,
    [property: JsonPropertyName("means")] Dictionary<string, Quantity> Means,
    [property: JsonPropertyName("label")] string? Label = null);

/// <summary>测量协方差原始记录。对角线=单维方差; 非对角=相关误差, 必须按协方差传播。</summary>
public sealed record CovarianceContent(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("dimensions")] List<string> Dimensions,
    [property: JsonPropertyName("matrix")] double[][] Matrix,
    [property: JsonPropertyName("scope")] string Scope,            // sample|endmember
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("units")] Dictionary<string, string> Units,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <summary>分馏版本。版本切换只影响引用它的派生结果, 不全局替换旧结论。</summary>
public sealed record FractionationContent(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("parentVersion")] string? ParentVersion,
    [property: JsonPropertyName("offsets")] Dictionary<string, Quantity> Offsets,
    [property: JsonPropertyName("rationale")] string? Rationale = null);

/// <summary>先验假设(比例区间); priorSupport=none 表示不可行先验。</summary>
public sealed record PriorContent(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("endmemberBounds")] Dictionary<string, Bounds> EndmemberBounds,
    [property: JsonPropertyName("notes")] string? Notes = null);

public sealed record Bounds(
    [property: JsonPropertyName("min")] double Min,
    [property: JsonPropertyName("max")] double Max);

public sealed record DimensionContent(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("datum")] string Datum);

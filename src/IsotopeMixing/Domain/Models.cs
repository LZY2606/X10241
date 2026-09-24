using System.Text.Json.Nodes;

namespace IsotopeMixing.Domain;

public enum RecordKind
{
    Sample,
    Endmember,
    Covariance,
    Fractionation,
    Prior,
    Note
}

/// <summary>
/// 一条来源记录。原始表示以 <see cref="Payload"/>（逐字 JSON 节点）与
/// <see cref="BlobSha256"/> 指向的原始字节保存；规范化只发生在派生层。
/// 稳定身份为 Ref = Key@Revision，修订通过 SupersedesRef 串联，不覆盖旧记录。
/// </summary>
public sealed record MaterialRecord(
    string Ref,
    string Key,
    int Revision,
    RecordKind Kind,
    string Branch,
    string? SupersedesRef,
    string UnitOriginal,
    string? UnitCanonical,
    string? Datum,
    string SourceLabel,
    JsonNode Payload,
    string BlobSha256,
    long Seq,
    DateTimeOffset ImportedAt)
{
    public string DisplayName => Payload["name"]?.GetValue<string>() ?? Key;
}

/// <summary>原始量：值、单位、原始字符串与无法转换时的诊断并存。</summary>
public sealed record RawQuantity(string? Raw, double? Value, string? Unit, string? Diagnostic)
{
    public static readonly RawQuantity Missing = new(null, null, null, "missing isotope dimension");
}

/// <summary>单个端元的一个模式（多模态分布产生多个候选可行区）。</summary>
public sealed record EndmemberMode(string Id, string Label, double[] Means, double[] Sigmas);

public sealed record EndmemberSource(string Ref, string Key, string Name, string Set, IReadOnlyList<EndmemberMode> Modes);

public sealed record SampleSource(
    string Ref, string Key, string Name, string EndmemberSet,
    IReadOnlyDictionary<string, RawQuantity> Isotopes,
    string? CovarianceRef, string? FractionationRef, string? PriorRef);

public sealed record CovarianceSource(
    string Ref, string Key, string Name, IReadOnlyList<string> Dims, double[,] Matrix, string Unit);

public sealed record FractionationSource(
    string Ref, string Key, string Name, string Version,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Shifts, string Unit);

public sealed record PriorSource(
    string Ref, string Key, string Name,
    IReadOnlyDictionary<string, (double? Min, double? Max)> Bounds);

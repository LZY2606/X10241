namespace IsoMix.Domain;

/// <summary>一条不可变的原始材料记录：来源与解释分离中的"来源"侧。</summary>
public sealed record RawRecord(
    string RecordId,
    string CaseId,
    string Kind,
    string SourceFile,
    string OriginalText,
    string ByteSha256,
    string ContentHash,
    string ImportedAt)
{
    public int Seq { get; init; }
    public string? SupersedesRecordId { get; init; }
}

/// <summary>整包原始字节（清单与全部文件的封装），按内容寻址，永不重写。</summary>
public sealed record PackBlob(string PackId, int PackVersion, string ByteSha256, string Base64Bytes, string ImportedAt);

/// <summary>无法完成单位/量纲转换的原始值就地保留，并附诊断；绝不丢弃。</summary>
public sealed record ConversionDiagnostic(
    string RecordId,
    string Field,
    string RawValue,
    string Problem,
    string Severity);

using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsoMix.Domain;

public enum SourceKind
{
    Sample,
    Endmember,
    Covariance,
    Fractionation,
    Units,
    Prior,
}

/// <summary>来源层原始记录：只保存，不做不可逆清洗。</summary>
public sealed record SourceRecord(
    string Id,
    string BatchId,
    SourceKind Kind,
    string Name,
    int Version,
    string? Unit,
    string? Basis,
    string RawJson,
    string? Diagnostic,
    string CreatedAt,
    int Seq);

public sealed record ImportBatchInfo(string Id, string StartedAt, string? FinishedAt, string Status, string? Error);

public sealed record BranchInfo(
    string Id,
    string? ParentId,
    string Name,
    string? BaseEventId,
    string HeadEventId,
    int Version,
    string CreatedAt);

public sealed record EventRecord(
    string Id,
    string BranchId,
    int Seq,
    string Type,
    string PayloadJson,
    string Actor,
    string CreatedAt);

public sealed record DependencyRef(string Name, int Version, string RecordId, SourceKind Kind);

public sealed record AnalysisResult(
    string Id,
    string EventId,
    string BranchId,
    string DepsJson,
    string FlagsJson,
    string OutputJson,
    string CreatedAt);

public sealed class ConcurrencyConflictException : Exception
{
    public string BranchId { get; }
    public int ExpectedVersion { get; }
    public int ActualVersion { get; }

    public ConcurrencyConflictException(string branchId, int expected, int actual)
        : base($"分支 {branchId} 版本冲突：期望 {expected}，实际 {actual}")
    {
        BranchId = branchId;
        ExpectedVersion = expected;
        ActualVersion = actual;
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static string Ser(object value) => JsonSerializer.Serialize(value, Options);

    public static T? Des<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
}

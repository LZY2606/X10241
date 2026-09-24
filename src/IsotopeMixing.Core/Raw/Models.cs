using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Core.Raw;

/// <summary>Kind of the source material record. The source and its interpretation are stored separately.</summary>
public enum RecordKind
{
    Sample,
    EndmemberSet,
    MeasurementCovariance,
    FractionationVersion,
    Prior,
    UnitFrame
}

/// <summary>
/// A value exactly as provided by the importing team. No conversion, rounding or cleaning
/// happens at this layer; normalization is a separate, derived layer that keeps a pointer back
/// to this token.
/// </summary>
public sealed class RawValue
{
    public string Dimension { get; set; } = "";
    public double? Numeric { get; set; }
    public string? Text { get; set; }
    public string? Unit { get; set; }
    public bool Present { get; set; } = true;

    /// <summary>Verbatim fragment of the original import (string form) for audit and re-parse.</summary>
    public string? OriginalToken { get; set; }
}

/// <summary>Reference frame for a delta/ratio measurement, kept with the raw material.</summary>
public sealed class UnitFrame
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Per-dimension default scale/unit, e.g. d13C -> VPDB permil.</summary>
    public Dictionary<string, string> DimensionUnits { get; set; } = new();
    public string CoordinateBasis { get; set; } = "";
    public string? Notes { get; set; }
}

public sealed class SampleRecord
{
    public List<RawValue> Values { get; set; } = new();
    public string? UnitFrameId { get; set; }
}

public sealed class EndmemberMember
{
    public string Name { get; set; } = "";
    public List<RawValue> Means { get; set; } = new();
    /// <summary>Optional per-endmember covariance rows keyed by dimension; null = pooled/none.</summary>
    public double[][]? Covariance { get; set; }
}

public sealed class EndmemberSetRecord
{
    public string? UnitFrameId { get; set; }
    public List<EndmemberMember> Members { get; set; } = new();
}

public sealed class MeasurementCovarianceRecord
{
    public List<string> Dimensions { get; set; } = new();
    public double[][] Matrix { get; set; } = System.Array.Empty<double[]>();
    /// <summary>Optional per-sample overrides: sampleId -> matrix.</summary>
    public Dictionary<string, double[][]>? PerSample { get; set; }
}

public sealed class FractionationVersionRecord
{
    /// <summary>Correction convention, e.g. "sample = sum f_i em_i + eps_i" (eps added to em).</summary>
    public string Convention { get; set; } = "eps_added_to_endmember";
    public List<string> Dimensions { get; set; } = new();
    /// <summary>Rows keyed by endmember name.</summary>
    public Dictionary<string, double[]> Offsets { get; set; } = new();
    /// <summary>
    /// When present, the factors can be routed to endmembers in several discrete ways.
    /// Each route is a list of endmember names aligned with Offsets rows; this is the source
    /// of legitimate multiple feasible regions.
    /// </summary>
    public List<List<string>>? Routes { get; set; }
}

public sealed class PriorRecord
{
    /// <summary>Box prior on proportions, one [min,max] per endmember (in declared order).</summary>
    public double[][] Bounds { get; set; } = System.Array.Empty<double[]>();
    /// <summary>If set, the prior only applies to this fractionation route index.</summary>
    public int? RouteIndex { get; set; }
}

/// <summary>
/// One piece of source material. The payload is the original interpreted form (parsed JSON
/// objects whose leaves are still <see cref="RawValue"/>), while the originating bytes live in
/// a referenced blob and are never modified.
/// </summary>
public sealed class RawRecord
{
    public string Id { get; set; } = "";
    public RecordKind Kind { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Monotonic within a material family; older records stay readable forever.</summary>
    public int Version { get; set; } = 1;
    public string? UnitFrameId { get; set; }
    public string? BlobId { get; set; }
    public string? SourcePath { get; set; }
    public string? SourceMediaType { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset ImportedAt { get; set; }
    public string ImportBatchId { get; set; } = "";

    public T PayloadAs<T>() => JsonUtil.Deserialize<T>(PayloadJson);
}

public sealed class RawBlob
{
    public string Id { get; set; } = "";
    public string MediaType { get; set; } = "application/octet-stream";
    public byte[] Sha256 { get; set; } = System.Array.Empty<byte>();
    public byte[] Bytes { get; set; } = System.Array.Empty<byte>();
    public DateTimeOffset StoredAt { get; set; }
}

public sealed class ImportBatch
{
    public string Id { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool Committed { get; set; }
    public string? Error { get; set; }
    public string? CaseId { get; set; }
}

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Opt);
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Opt) ?? throw new InvalidDataException("null payload");
}

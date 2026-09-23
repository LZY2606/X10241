using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing;

public static class StableIds
{
    public static string Hash(string prefix, string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return $"{prefix}.{Convert.ToHexString(bytes, 0, 12).ToLowerInvariant()}";
    }

    public static string New(string prefix) => Hash(prefix, Guid.NewGuid().ToString("N") + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
}

public record Dimension(string Code, string RawUnit, string? CoordinateDatum, double? Value, bool Convertible, string? Diagnostic);

public record NamedValue(string StableId, string Code, double? Value, string Raw, string Unit, string? CoordinateDatum, string? Diagnostic);

public record SourceMaterial(
    string Kind,
    string StableId,
    string Label,
    IReadOnlyList<NamedValue> Values,
    string? SampleId = null,
    double Weight = 1,
    string? ModeGroup = null);

public record CovarianceCell(string SampleId, string Row, string Column, double Value, string Raw, string Unit, string? CoordinateDatum, string? Diagnostic);

public record FractionationValue(string Code, double? Value, string Raw, string Unit, string? CoordinateDatum, string? Diagnostic);

public record PriorBound(string EndmemberId, double? Minimum, double? Maximum, string RawMinimum, string RawMaximum, string Rule);

public record BranchRecord(string Id, string Name, string? ParentBranchId, string? ForkFromEventId, long CreatedAtUnix, string? Purpose);

public record EventRecord(
    long Seq,
    string Id,
    string BranchId,
    string Type,
    string PayJson,
    string? ParentEventId,
    string? BasisEventId,
    string Actor,
    long CreatedAtUnix,
    string? RevokedEventId);

public record InterpretationRecord(
    string Id,
    string BranchId,
    string Kind,
    string StableId,
    int Version,
    string? ParentInterpretationId,
    string SourceRecordId,
    string PayJson,
    string DependencyHash,
    string Status,
    string? InvalidatedByEventId,
    long CreatedAtUnix);

public record RawRecord(string Id, string Kind, string SourceUri, string ContentHash, string RawJson, long ImportSeq, long CreatedAtUnix);

public record AnalysisInput
{
    public required string AnalysisId { get; init; }
    public required string SampleId { get; init; }
    public required string SampleLabel { get; init; }
    public required string FractionationVersionId { get; init; }
    public required string PriorId { get; init; }
    public required IReadOnlyList<string> Dimensions { get; init; }
    public required double[] Sample { get; init; }
    public required double[,] Covariance { get; init; }
    public required EndmemberModeGroup[] ModesByEndmember { get; init; }
    public required PriorBound[] Priors { get; init; }
    public required IReadOnlyList<NamedValue> RawSampleValues { get; init; }
    public required IReadOnlyList<CovarianceCell> RawCovariance { get; init; }
}

public record EndmemberModeGroup(string EndmemberId, string Label, IReadOnlyList<EndmemberMode> Modes);

public record EndmemberMode(string EndmemberId, string ModeId, string Label, double[] Values, double Weight);

public record FeasiblePoint(string StableId, double[] Proportions, double Mahalanobis, double[] Residual);

public record FeasibleRegion(
    string StableId,
    string ModeKey,
    IReadOnlyList<string> ModeIds,
    string Status,
    IReadOnlyList<FeasiblePoint> Points,
    IReadOnlyList<double> MinProportions,
    IReadOnlyList<double> MaxProportions,
    IReadOnlyList<FeasiblePoint> Vertices);

public record AnalysisResult(
    string StableId,
    string Status,
    string Identifiability,
    string Rationale,
    IReadOnlyList<string> ObservedDimensions,
    IReadOnlyList<string> MissingDimensions,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<FeasibleRegion> Regions,
    IReadOnlyList<string> DependencyStableIds,
    double[]? LeastSquaresProportions,
    double[]? ClosestResidual,
    double? ConditionRatio,
    double ChiSquareLimit,
    IReadOnlyList<MachineJudgment> Judgments);

public record MachineJudgment(string Code, string Severity, string Message, IReadOnlyDictionary<string, string> Evidence);

public record UnitConclusion(bool Convertible, double? Value, string? CanonicalUnit, string? Diagnostic);

public static class UnitConversions
{
    private static readonly HashSet<string> Permil = new(StringComparer.OrdinalIgnoreCase)
    {
        "permille", "per_mille", "‰", "o/oo", "permil", "permil_vpdb", "vpdb_permil"
    };

    public static UnitConclusion Convert(NamedValue value)
    {
        if (!double.TryParse(value.Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new UnitConclusion(false, null, null, $"RAW-NUMERIC: {value.Code} 的原始表示 `{value.Raw}` 不是可解析数值，已保留原位。");
        if (string.IsNullOrWhiteSpace(value.Unit) || Permil.Contains(value.Unit.Trim()))
            return new UnitConclusion(true, number, "permille", null);
        return new UnitConclusion(false, number, null, $"UNIT-UNSUPPORTED: {value.Code} 的单位 `{value.Unit}` 没有安全换算规则，未为绘图丢弃。");
    }

    public static UnitConclusion ConvertCell(CovarianceCell cell)
    {
        if (Permil.Contains(cell.Unit.Trim()) || string.IsNullOrWhiteSpace(cell.Unit))
            return new UnitConclusion(true, cell.Value, "permille^2", null);
        return new UnitConclusion(false, cell.Value, null, $"UNIT-UNSUPPORTED: {cell.Row}/{cell.Column} 协方差单位 `{cell.Unit}` 不可转换。");
    }
}

using IsotopeMixing.Core.Solver;

namespace IsotopeMixing.Core.Raw;

public sealed record NormalizeDiagnostic(
    string Code,
    string Severity,
    string RecordId,
    string? Dimension,
    string Message,
    string? OriginalToken);

/// <summary>
/// Derived layer only: converts the preserved raw records into canonical numeric form
/// (permil delta values on the documented scales). Raw records are never rewritten.
/// Values that cannot be converted stay at the original position as a diagnostic instead
/// of being dropped to make plotting possible.
/// </summary>
public sealed class Normalizer
{
    public const string DefaultCovFrame = "VPDB/permille-coordinate";

    private static readonly Dictionary<string, double> PermilPct = new()
    {
        ["permille"] = 1.0, ["permil"] = 1.0, ["\u2030"] = 1.0,
        ["percent"] = 10.0, ["pct"] = 10.0, ["%"] = 10.0
    };

    public NormalizedBundle Normalize(IEnumerable<RawRecord> records, out List<NormalizeDiagnostic> diagnostics)
    {
        var list = records.ToList();
        diagnostics = new List<NormalizeDiagnostic>();

        var frames = list.Where(r => r.Kind == RecordKind.UnitFrame)
            .ToDictionary(r => r.Id, r => r.PayloadAs<UnitFrame>());

        var bundle = new NormalizedBundle();

        foreach (var rec in list.Where(r => r.Kind == RecordKind.Sample))
        {
            var sample = rec.PayloadAs<SampleRecord>();
            var values = ConvertValues(rec, sample.Values, sample.UnitFrameId, frames, diagnostics);
            bundle.Samples.Add(new NormalizedSample(rec.Id, rec.Label, rec.Version, values));
        }

        foreach (var rec in list.Where(r => r.Kind == RecordKind.EndmemberSet))
        {
            var ems = rec.PayloadAs<EndmemberSetRecord>();
            var members = new List<NormalizedEndmember>();
            foreach (var m in ems.Members)
            {
                var means = ConvertValues(rec, m.Means, ems.UnitFrameId, frames, diagnostics);
                members.Add(new NormalizedEndmember(m.Name, means, m.Covariance));
            }
            bundle.EndmemberSets.Add(new NormalizedEndmemberSet(rec.Id, rec.Label, rec.Version, members));
        }

        foreach (var rec in list.Where(r => r.Kind == RecordKind.MeasurementCovariance))
        {
            var cov = rec.PayloadAs<MeasurementCovarianceRecord>();
            bundle.Covariances.Add(new NormalizedCovariance(rec.Id, rec.Label, rec.Version,
                cov.Dimensions, cov.Matrix, cov.PerSample));
        }

        foreach (var rec in list.Where(r => r.Kind == RecordKind.FractionationVersion))
        {
            var fx = rec.PayloadAs<FractionationVersionRecord>();
            bundle.Fractionations.Add(new NormalizedFractionation(rec.Id, rec.Label, rec.Version,
                fx.Convention, fx.Dimensions, fx.Offsets, fx.Routes));
        }

        foreach (var rec in list.Where(r => r.Kind == RecordKind.Prior))
        {
            var prior = rec.PayloadAs<PriorRecord>();
            bundle.Priors.Add(new NormalizedPrior(rec.Id, rec.Label, rec.Version, prior.Bounds, prior.RouteIndex));
        }

        return bundle;
    }

    private static Dictionary<string, NormalizedValue> ConvertValues(
        RawRecord rec,
        IEnumerable<RawValue> rawValues,
        string? frameId,
        IReadOnlyDictionary<string, UnitFrame> frames,
        List<NormalizeDiagnostic> diagnostics)
    {
        frames.TryGetValue(frameId ?? "", out var frame);
        var result = new Dictionary<string, NormalizedValue>();

        foreach (var raw in rawValues)
        {
            if (!raw.Present || raw.Numeric is null)
            {
                diagnostics.Add(new NormalizeDiagnostic(
                    "missing-isotope-dimension", "warn", rec.Id, raw.Dimension,
                    $"dimension {raw.Dimension} absent in record {rec.Id}; kept as a gap, never imputed",
                    raw.OriginalToken));
                result[raw.Dimension] = new NormalizedValue(raw.Dimension, double.NaN, raw.Unit, false);
                continue;
            }

            var value = raw.Numeric.Value;
            var fromUnit = (raw.Unit ?? frame?.DimensionUnits.GetValueOrDefault(raw.Dimension) ?? "permille")
                .Trim().ToLowerInvariant();

            if (PermilPct.TryGetValue(fromUnit, out var factor))
            {
                result[raw.Dimension] = new NormalizedValue(raw.Dimension, value * factor, "permille", true);
            }
            else
            {
                diagnostics.Add(new NormalizeDiagnostic(
                    "unconvertible-unit", "error", rec.Id, raw.Dimension,
                    $"value {value} '{fromUnit}' in {rec.Id} has no conversion to permille; left in place",
                    raw.OriginalToken));
                result[raw.Dimension] = new NormalizedValue(raw.Dimension, double.NaN, fromUnit, false);
            }
        }

        return result;
    }
}

public sealed record NormalizedValue(string Dimension, double Value, string Unit, bool Usable);

public sealed record NormalizedSample(
    string RecordId, string Label, int Version,
    IReadOnlyDictionary<string, NormalizedValue> Values);

public sealed record NormalizedEndmember(
    string Name,
    IReadOnlyDictionary<string, NormalizedValue> Means,
    double[][]? Covariance);

public sealed record NormalizedEndmemberSet(
    string RecordId, string Label, int Version,
    IReadOnlyList<NormalizedEndmember> Members);

public sealed record NormalizedCovariance(
    string RecordId, string Label, int Version,
    IReadOnlyList<string> Dimensions,
    double[][] Matrix,
    Dictionary<string, double[][]>? PerSample);

public sealed record NormalizedFractionation(
    string RecordId, string Label, int Version,
    string Convention,
    IReadOnlyList<string> Dimensions,
    IReadOnlyDictionary<string, double[]> Offsets,
    IReadOnlyList<List<string>>? Routes);

public sealed record NormalizedPrior(
    string RecordId, string Label, int Version,
    double[][] Bounds,
    int? RouteIndex);

public sealed class NormalizedBundle
{
    public List<NormalizedSample> Samples { get; } = new();
    public List<NormalizedEndmemberSet> EndmemberSets { get; } = new();
    public List<NormalizedCovariance> Covariances { get; } = new();
    public List<NormalizedFractionation> Fractionations { get; } = new();
    public List<NormalizedPrior> Priors { get; } = new();
}

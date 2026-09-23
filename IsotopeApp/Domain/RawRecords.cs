using System.Text.Json;

namespace IsotopeApp.Domain;

/// <summary>A single observed isotope value, exactly as imported.</summary>
public sealed record IsotopeValue(
    string Tracer,
    string RawToken,
    double? Numeric,
    MeasureUnit Unit,
    string? Note)
{
    public bool IsMissing => Numeric is null;
}

/// <summary>One Gaussian mode of a (possibly mixture) endmember distribution.</summary>
public sealed record SourceMode(double[] Mean, double[,] Covariance, double Weight);

/// <summary>
/// An endmember ("source composition chamber" entry). A source is allowed to
/// carry several Gaussian modes, which is what makes a feasible set a union of
/// convex regions ("multiple feasible areas").
/// </summary>
public sealed record SourceRecord(
    string SourceId,
    string Name,
    IReadOnlyList<string> Tracers,
    IReadOnlyList<SourceMode> Modes,
    string MeansRaw,
    string CovarianceRaw)
{
    public bool IsMixture => Modes.Count > 1;
}

/// <summary>Measurement covariance for a sample, off-diagonals preserved.</summary>
public sealed record MeasurementCovariance(
    IReadOnlyList<string> Tracers,
    double[,] Matrix,
    string RawToken)
{
    public bool IsCorrelated
    {
        get
        {
            for (int i = 0; i < Matrix.GetLength(0); i++)
                for (int j = i + 1; j < Matrix.GetLength(1); j++)
                    if (Matrix[i, j] != 0) return true;
            return false;
        }
    }
}

/// <summary>
/// A fractionation version. Effective endmember position becomes mean + f for
/// every tracer; switching versions is an explicit parameter revision, never an
/// in-place overwrite of the old result.
/// </summary>
public sealed record FractionationVersion(
    string FractionationId,
    string Label,
    IReadOnlyDictionary<string, double> Shifts,
    string Basis,
    string RawToken)
{
    public static readonly FractionationVersion Zero =
        new("frac-none", "no fractionation", new Dictionary<string, double>(), "identity", "{}");
}

/// <summary>Prior over proportions. Box or simplex forms; checked, never forced.</summary>
public sealed record PriorAssumption(
    string PriorId,
    string Kind,
    IReadOnlyDictionary<string, (double Low, double High)> Bounds,
    string RawToken);

/// <summary>A measured sample with its covariance and raw representation.</summary>
public sealed class SampleRecord
{
    public required string SampleId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<IsotopeValue> Values { get; init; }
    public MeasurementCovariance? Covariance { get; init; }
    public string RawJson { get; init; } = "{}";
}

/// <summary>A diagnostic attached to an imported value that cannot be converted.</summary>
public sealed record ImportDiagnostic(string Severity, string ObjectId, string Field, string Message);

public static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}

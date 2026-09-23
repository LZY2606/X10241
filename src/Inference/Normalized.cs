using IsotopeMix.Model;

namespace IsotopeMix.Inference;

public sealed class AxisProblem
{
    public string Axis { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Basis { get; set; } = "";
    public string? Diagnostic { get; set; }
}

public sealed class NormalizedSample
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Group { get; init; } = "";
    public double[]? Y { get; init; }
    public double[,]? Sy { get; init; }
    public List<string> Axes { get; init; } = new();
    public List<AxisProblem> Problems { get; init; } = new();
    public Dictionary<string, RawCell> RawMeasurements { get; init; } = new();
}

public sealed class NormalizedSource
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public double[]? Mean { get; init; }
    public double[,]? Cov { get; init; }
    public List<string> Axes { get; init; } = new();
    public List<AxisProblem> Problems { get; init; } = new();
}

public sealed class NormalizedFractionation
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Version { get; init; }
    public string Convention { get; init; } = "";
    // delta per [axis, source]
    public double[,]? Delta { get; init; }
    public double[,]? Uncertainty { get; init; }
    public List<string> Axes { get; init; } = new();
    public List<string> SourceIds { get; init; } = new();
    public List<AxisProblem> Problems { get; init; } = new();
}

// Normalization happens here, in the derived layer. The imported facts are
// untouched. Values that cannot be converted stay in place as problems and are
// excluded from the numeric design rather than silently dropped.
public static class Normalizer
{
    public static NormalizedSample NormalizeSample(SampleDoc doc, IReadOnlyList<string> axes)
    {
        var problems = new List<AxisProblem>();
        var values = new List<double>();
        var present = new List<string>();
        var raw = new Dictionary<string, RawCell>();
        bool complete = true;
        foreach (var axis in axes)
        {
            if (!doc.Measurements.TryGetValue(axis, out var cell))
            {
                complete = false;
                continue;
            }
            raw[axis] = cell;
            if (!cell.Convertible || cell.Parsed is null)
            {
                complete = false;
                problems.Add(new AxisProblem { Axis = axis, Unit = cell.Unit, Basis = cell.Basis,
                    Diagnostic = cell.Diagnostic ?? "unconvertible value retained in raw layer" });
                continue;
            }
            values.Add(ConvertToPermille(cell.Parsed.Value, cell.Unit, cell.Basis, out var diag));
            present.Add(axis);
            if (diag is not null)
                problems.Add(new AxisProblem { Axis = axis, Unit = cell.Unit, Basis = cell.Basis, Diagnostic = diag });
        }

        double[,]? cov = null;
        if (doc.Covariance is not null && complete)
            cov = ReadCovariance(doc.Covariance, axes, problems, out _);
        else if (doc.Covariance is not null && present.Count > 0)
            cov = ReadCovariance(doc.Covariance, present, problems, out _);

        return new NormalizedSample
        {
            Id = doc.StableId, Label = doc.Label, Group = doc.Group,
            Y = complete ? values.ToArray() : null,
            Sy = cov,
            Axes = complete ? axes.ToList() : present,
            Problems = problems,
            RawMeasurements = raw
        };
    }

    public static NormalizedSource NormalizeSource(SourceDoc doc, IReadOnlyList<string> axes)
    {
        var problems = new List<AxisProblem>();
        var values = new List<double>();
        bool complete = true;
        foreach (var axis in axes)
        {
            if (!doc.Means.TryGetValue(axis, out var cell))
            {
                complete = false;
                problems.Add(new AxisProblem { Axis = axis, Diagnostic = "source mean missing for axis" });
                continue;
            }
            if (!cell.Convertible || cell.Parsed is null)
            {
                complete = false;
                problems.Add(new AxisProblem { Axis = axis, Unit = cell.Unit, Basis = cell.Basis,
                    Diagnostic = cell.Diagnostic ?? "unconvertible value" });
                continue;
            }
            values.Add(ConvertToPermille(cell.Parsed.Value, cell.Unit, cell.Basis, out var diag));
            if (diag is not null) problems.Add(new AxisProblem { Axis = axis, Diagnostic = diag });
        }
        double[,]? cov = doc.Covariance is not null && complete
            ? ReadCovariance(doc.Covariance, axes, problems, out _) : null;
        return new NormalizedSource
        {
            Id = doc.StableId, Label = doc.Label,
            Mean = complete ? values.ToArray() : null, Cov = cov,
            Axes = complete ? axes.ToList() : new(), Problems = problems
        };
    }

    public static NormalizedFractionation NormalizeFractionation(FractionationDoc doc,
        IReadOnlyList<string> sourceIds, IReadOnlyList<string> axes)
    {
        var problems = new List<AxisProblem>();
        var delta = new double[axes.Count, sourceIds.Count];
        var unc = new double[axes.Count, sourceIds.Count];
        bool complete = true;
        for (int j = 0; j < sourceIds.Count; j++)
        for (int i = 0; i < axes.Count; i++)
        {
            double v = 0, u = 0;
            if (doc.Factors.TryGetValue(sourceIds[j], out var byAxis) &&
                byAxis.TryGetValue(axes[i], out var cell))
            {
                if (cell.Convertible && cell.Parsed is not null)
                    v = ConvertToPermille(cell.Parsed.Value, cell.Unit, cell.Basis, out var diag);
                else { complete = false; problems.Add(new AxisProblem { Axis = axes[i], Diagnostic = cell.Diagnostic }); }
                if (doc.Uncertainty.TryGetValue(sourceIds[j], out var uMap) &&
                    uMap.TryGetValue(axes[i], out var uCell) && uCell.Parsed is not null)
                    u = ConvertToPermille(uCell.Parsed.Value, uCell.Unit, uCell.Basis, out _);
            }
            delta[i, j] = v; unc[i, j] = u;
        }
        return new NormalizedFractionation
        {
            Id = doc.StableId, Label = doc.Label, Version = doc.Version, Convention = doc.Convention,
            Delta = complete ? delta : null, Uncertainty = unc,
            Axes = axes.ToList(), SourceIds = sourceIds.ToList(), Problems = problems
        };
    }

    // Keeps the original representation; only known isotope scales are normalized.
    // permille/VPDB, permille/AIR and permille are identity; fraction is x1000.
    private static double ConvertToPermille(double value, string unit, string basis, out string? diagnostic)
    {
        diagnostic = null;
        switch ((unit ?? "").Trim().ToLowerInvariant())
        {
            case "‰":
            case "permil":
            case "per mille":
            case "per-mille":
                return value;
            case "fraction":
            case "r_ratio_delta_fraction":
                return value * 1000.0;
            default:
                diagnostic = $"unit '{unit}' not recognized; value kept in raw layer, not normalized";
                return value;
        }
    }

    private static double[,]? ReadCovariance(CovData data, IReadOnlyList<string> axes,
        List<AxisProblem> problems, out bool correlated)
    {
        correlated = false;
        int n = axes.Count;
        var cov = new double[n, n];
        var index = data.Axes.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i);
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            if (!index.TryGetValue(axes[i], out int ri) || data.Rows.Count <= ri ||
                data.Rows[ri].Count <= index[axes[j]])
            { cov[i, j] = i == j ? double.NaN : 0; continue; }
            var cell = data.Rows[ri][index[axes[j]]];
            cov[i, j] = cell.Parsed ?? double.NaN;
        }
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
            if (!double.IsNaN(cov[i, j]) && cov[i, j] != 0) correlated = true;
        if (data.Rows.Count >= n && !string.IsNullOrWhiteSpace(data.CorrelatedNote)) correlated = true;
        return cov;
    }
}

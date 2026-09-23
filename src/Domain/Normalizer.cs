using System.Globalization;
using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

/// <summary>一次分析请求：固定样品、端元集合、分馏版本、先验与维列表。</summary>
public sealed record AnalysisRequest
{
    public required string SampleId { get; init; }
    public required IReadOnlyList<string> EndmemberIds { get; init; }
    public required string FractionationId { get; init; }
    public required string PriorId { get; init; }
    public string? Title { get; init; }
    public SolverOptions? Options { get; init; }
}

public sealed record NormalizedInputs
{
    public required MixingProblem Problem { get; init; }
    public required IReadOnlyList<string> MissingDimensions { get; init; }
    public required IReadOnlyList<string> ConversionDiagnostics { get; init; }
}

public static class Normalizer
{
    public static Quantity ParseQuantity(string raw, string expectedUnit, string? datum)
    {
        string text = raw.Trim();
        string unit = expectedUnit;
        string? diag = null;
        double? value = null;

        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"^\s*([+-]?(?:\d+\.\d+|\.\d+|\d+)(?:[eE][+-]?\d+)?)\s*(\S*)");
        if (!match.Success)
        {
            diag = $"UNCONVERTIBLE_VALUE: 原始表示 \"{raw}\" 无法解析为数值，已原样保留，未用于绘图/求解";
            return new Quantity(raw, null, unit, datum, diag);
        }
        string num = match.Groups[1].Value;
        string attached = match.Groups[2].Value;
        double parsed;
        try
        {
            parsed = double.Parse(num, CultureInfo.InvariantCulture);
        }
        catch
        {
            diag = $"UNCONVERTIBLE_VALUE: 原始表示 \"{raw}\" 无法解析为数值，已原样保留";
            return new Quantity(raw, null, unit, datum, diag);
        }

        if (!string.IsNullOrEmpty(attached))
        {
            var converted = ConvertUnit(parsed, attached, expectedUnit, out string? unitDiag);
            if (unitDiag is not null)
            {
                diag = unitDiag;
            }
            else
            {
                parsed = converted;
                unit = attached;
            }
        }
        if (diag is not null) return new Quantity(raw, null, unit, datum, diag);
        return new Quantity(raw, parsed, expectedUnit, datum);
    }

    private static double ConvertUnit(double v, string from, string to, out string? diag)
    {
        diag = null;
        from = from.Replace("‰", "permil").Replace("ℏ", "permil");
        to = to.Replace("‰", "permil");
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return v;
        var equiv = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["permil"] = new[] { "permil", "permille", "ppt" },
            ["fraction"] = new[] { "fraction", "frac", "proportion" }
        };
        foreach (var kv in equiv)
        {
            bool f = kv.Value.Contains(from.ToLowerInvariant());
            bool t = kv.Value.Contains(to.ToLowerInvariant());
            if (f && t) return v;
        }
        if (from.ToLowerInvariant() is "permil" or "permille" or "ppt"
            && to.ToLowerInvariant() is "fraction" or "frac")
            return v / 1000.0;
        if (to.ToLowerInvariant() is "permil" or "permille" or "ppt"
            && from.ToLowerInvariant() is "fraction" or "frac")
            return v * 1000.0;
        diag = $"UNCONVERTIBLE_UNIT: 单位 \"{from}\" 与目标单位 \"{to}\" 之间没有已知换算，原始值保留在原位置";
        return v;
    }

    public static NormalizedInputs BuildProblem(
        string problemId,
        RawSample sample,
        IReadOnlyList<RawEndmember> endmembers,
        FractionationSet fractionation,
        PriorAssumption prior,
        IReadOnlyList<DimensionSpec> dimensionSpecs,
        SolverOptions? options = null)
    {
        var diags = new List<string>();
        var usableDims = new List<string>();
        var missing = new List<string>();
        foreach (var dim in dimensionSpecs)
        {
            bool ok = sample.Values.TryGetValue(dim.Code, out var q) && q is not null && q.Convertible;
            bool endOk = true;
            foreach (var em in endmembers)
            {
                var mode = em.Modes[0];
                if (!mode.Means.TryGetValue(dim.Code, out var eq) || eq is null || !eq.Convertible)
                {
                    endOk = false; break;
                }
            }
            if (ok && endOk) usableDims.Add(dim.Code);
            else
            {
                missing.Add(dim.Code);
                if (q is { Convertible: false } sq)
                    diags.Add($"SAMPLE_VALUE_UNCONVERTIBLE[{sample.StableId}/{dim.Code}]: {sq.Diagnostic}");
            }
        }

        int d = usableDims.Count;
        int m = endmembers.Count;
        var mean = new double[d];
        for (int i = 0; i < d; i++)
            mean[i] = sample.Values[usableDims[i]]!.Value!.Value;

        var sampleCov = Statistics.AlignCovariance(sample.Covariance, usableDims)
            ?? FallbackDiagonal(usableDims, sample, diags);

        var modes = new List<IReadOnlyList<(string, string, double[], double[,])>>();
        foreach (var em in endmembers)
        {
            var list = new List<(string, string, double[], double[,])>();
            foreach (var mode in em.Modes)
            {
                var emMean = new double[d];
                for (int i = 0; i < d; i++)
                    emMean[i] = mode.Means[usableDims[i]]!.Value!.Value;
                var cov = Statistics.AlignCovariance(mode.Covariance, usableDims)
                    ?? FallbackDiagonal(usableDims, mode.ModeId, diags);
                list.Add((mode.ModeId, mode.Label, emMean, cov));
            }
            modes.Add(list);
        }

        var frac = new double[m, d];
        for (int k = 0; k < m; k++)
            for (int i = 0; i < d; i++)
                frac[k, i] = fractionation.Offsets
                    .GetValueOrDefault(endmembers[k].StableId)?
                    .GetValueOrDefault(usableDims[i]) ?? 0.0;

        var bounds = new List<(double?, double?)>();
        foreach (var em in endmembers)
            bounds.Add(prior.Bounds.GetValueOrDefault(em.StableId, (null, null)));

        var problem = new MixingProblem
        {
            ProblemId = problemId,
            Dimensions = usableDims,
            EndmemberIds = endmembers.Select(e => e.StableId).ToList(),
            SampleMean = mean,
            SampleCovariance = sampleCov,
            EndmemberModes = modes,
            FractionationOffsets = frac,
            PriorBounds = bounds,
            Options = options ?? new SolverOptions()
        };
        return new NormalizedInputs(problem, missing, diags);
    }

    private static double[,] FallbackDiagonal(IReadOnlyList<string> dims, object owner, List<string> diags)
    {
        diags.Add($"COVARIANCE_MISSING_DIMENSION[{owner}]: 协方差块未覆盖全部可用维度，相关误差信息不完整，该维回退为对角小方差并标记诊断");
        var cov = new double[dims.Count, dims.Count];
        for (int i = 0; i < dims.Count; i++) cov[i, i] = 1e-6;
        return cov;
    }
}

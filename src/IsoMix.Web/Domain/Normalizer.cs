namespace IsoMix.Domain;

public sealed record Dimension(string Isotope, string UnitFamily, string CanonicalUnit);

public sealed record NormalizedCase(
    RawCase Raw,
    string Scenario,
    IReadOnlyList<Dimension> Dimensions,
    double[] Sample,
    double[][] SampleCovariance,
    double[][] EndmemberMeans,
    double[][][] EndmemberCovariances,
    double[] FractionationDelta,
    double[][]? PriorIntervals,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<DimensionIssue> Issues);

public sealed record DimensionIssue(string Isotope, string Reason, string Detail);

public static class Normalizer
{
    public static NormalizedCase Normalize(RawCase raw)
    {
        var diagnostics = new List<string>();
        var issues = new List<DimensionIssue>();

        var isotopeOrder = raw.Sample.Observations.Select(o => o.Isotope).Distinct().ToList();

        var sampleMap = raw.Sample.Observations.ToDictionary(o => o.Isotope);
        var emMaps = raw.Endmembers
            .Select(e => e.Observations.ToDictionary(o => o.Isotope))
            .ToList();
        var fracMap = raw.Fractionation.Entries.ToDictionary(e => e.Isotope);

        var kept = new List<Dimension>();
        var keepIdx = new List<int>();

        for (var i = 0; i < isotopeOrder.Count; i++)
        {
            var iso = isotopeOrder[i];

            if (sampleMap.TryGetValue(iso, out var s) && s.Value is null)
            {
                issues.Add(new DimensionIssue(iso, RunFlags.MissingIsotope,
                    $"sample value not parseable or absent, raw token '{s.RawToken}' kept at original position"));
                diagnostics.Add($"{RunFlags.MissingIsotope}: {iso} (sample)");
                continue;
            }

            var emMissing = false;
            foreach (var (em, m) in raw.Endmembers.Zip(emMaps))
            {
                if (!m.TryGetValue(iso, out var obs) || obs.Value is null)
                {
                    var token = m.TryGetValue(iso, out var o) ? o.RawToken : "<absent>";
                    issues.Add(new DimensionIssue(iso, RunFlags.MissingIsotope,
                        $"endmember '{em.Name}' value absent, raw token '{token}' kept at original position"));
                    diagnostics.Add($"{RunFlags.MissingIsotope}: {iso} (endmember {em.Name})");
                    emMissing = true;
                }
            }
            if (emMissing) continue;

            var units = new List<string?> { sampleMap[iso].Unit };
            units.AddRange(emMaps.Select(m => m[iso].Unit));
            if (fracMap.TryGetValue(iso, out var frac)) units.Add(frac.Unit);

            string? family = null;
            string? canonical = null;
            var unitBad = false;
            foreach (var u in units)
            {
                if (!UnitRegistry.TryResolve(u, out var c))
                {
                    issues.Add(new DimensionIssue(iso, RunFlags.UnconvertibleValue,
                        $"unrecognized or unconvertible unit '{u}'; value left in place, dimension excluded from derived math"));
                    diagnostics.Add($"{RunFlags.UnconvertibleValue}: {iso} unit '{u}'");
                    unitBad = true;
                    break;
                }
                family ??= c.Family;
                canonical ??= c.Canonical;
                if (c.Family != family)
                {
                    issues.Add(new DimensionIssue(iso, RunFlags.UnconvertibleValue,
                        $"mixed reference standards ({family} vs {c.Family}); dimension excluded from derived math, originals retained"));
                    diagnostics.Add($"{RunFlags.UnconvertibleValue}: {iso} mixed standards");
                    unitBad = true;
                    break;
                }
            }
            if (unitBad) continue;

            kept.Add(new Dimension(iso, family!, canonical!));
            keepIdx.Add(i);
        }

        if (kept.Count == 0)
            throw new InvalidOperationException("no usable isotope dimensions remain after normalization; raw records are retained");

        var d = kept.Count;
        double Val(Dictionary<string, RawObservation> map, string iso) => map[iso].Value!.Value;

        var sample = new double[d];
        var endMeans = new double[raw.Endmembers.Count][];
        for (var j = 0; j < raw.Endmembers.Count; j++) endMeans[j] = new double[d];
        var delta = new double[d];

        for (var p = 0; p < d; p++)
        {
            var iso = kept[p].Isotope;
            sample[p] = Val(sampleMap, iso);
            for (var j = 0; j < raw.Endmembers.Count; j++) endMeans[j][p] = Val(emMaps[j], iso);
            delta[p] = fracMap.TryGetValue(iso, out var f) && f.Value is double fv ? fv : 0.0;
        }

        var sampleCov = RestrictMatrix(raw.Sample.Covariance, keepIdx);
        var emCovs = raw.Endmembers.Select(e => RestrictMatrix(e.Covariance, keepIdx)).ToArray();

        double[][]? priors = null;
        if (raw.Prior is not null)
        {
            priors = new double[d][];
            var rawIntervals = raw.Prior.Intervals;
            var pvals = raw.Prior.Values;
            var usable = true;
            for (var p = 0; p < d && usable; p++)
            {
                var gi = keepIdx[p];
                if (pvals is null || gi >= pvals.Length) { usable = false; break; }
                var row = pvals[gi];
                if (row.Length < 2 || row[0] is null || row[1] is null) { usable = false; break; }
                priors[p] = new[] { row[0]!.Value, row[1]!.Value };
            }
            if (!usable)
            {
                diagnostics.Add($"{RunFlags.PriorInfeasible}: prior intervals unparseable at original position, prior ignored");
                priors = null;
            }
        }

        return new NormalizedCase(raw, raw.Scenario, kept, sample, sampleCov, endMeans, emCovs, delta,
            priors, diagnostics, issues);
    }

    private static double[][] RestrictMatrix(RawMatrix m, List<int> keep)
    {
        var d = keep.Count;
        var r = new double[d][];
        var vals = m.Values;
        for (var a = 0; a < d; a++)
        {
            r[a] = new double[d];
            for (var b = 0; b < d; b++)
            {
                var ia = keep[a];
                var ib = keep[b];
                var v = vals is not null && ia < vals.Length && vals[ia] is { Length: > 0 } row && ib < row.Length
                    ? row[ib]
                    : null;
                r[a][b] = v ?? 0.0;
            }
        }
        return r;
    }
}

using MixingStudio.Domain;

namespace MixingStudio.Math;

public static class DerivationStatus
{
    public const string Ok = "Ok";
    public const string IdentifiabilityInsufficient = "IdentifiabilityInsufficient";
    public const string SampleOutsideEnvelope = "SampleOutsideEnvelope";
    public const string InfeasiblePrior = "InfeasiblePrior";
    public const string NoFeasibleRegion = "NoFeasibleRegion";
}

public sealed record Diagnostic(string Code, string Message, string? Dim);

public sealed record FeasibleRegion(
    int Index,
    List<double[]> Points,          // barycentric points on the simplex, all sum=1, all >= 0
    double[] Representative,        // min-D2 point; explicitly NOT a unique answer
    double MinD2,
    double[] Residual);             // isotope-space residual at representative point

public sealed record DerivationResult(
    string Status,
    double ConditionNumber,
    bool Identifiable,
    List<string> Dims,
    List<FeasibleRegion> Regions,
    List<Diagnostic> Diagnostics,
    string FractionationVersion,
    string EndmemberVersion)
{
    public object ToEventPayload() => new
    {
        status = Status,
        conditionNumber = ConditionNumber,
        identifiable = Identifiable,
        dims = Dims,
        regionCount = Regions.Count,
        regions = Regions.Select(r => new
        {
            index = r.Index,
            pointCount = r.Points.Count,
            representative = r.Representative,
            minD2 = r.MinD2,
            residual = r.Residual
        }),
        diagnostics = Diagnostics.Select(d => new { d.Code, d.Message, d.Dim }),
        fractionationVersion = FractionationVersion,
        endmemberVersion = EndmemberVersion,
        constraintPolicy = "simplex-native: feasibility evaluated only at points with sum=1 and f>=0; no truncation-then-renormalize",
        errorPropagation = "full covariance: correlated errors propagated via Mahalanobis distance"
    };
}

public static class MixingModel
{
    private static readonly double[] Chi2_95 = { 0, 3.841, 5.991, 7.815, 9.488 };

    public const double CollinearityTolerance = 1e-2;

    /// <summary>
    /// Compute the feasible proportion region on the simplex.
    /// sum(f)=1 and f>=0 are enforced natively: feasibility is only ever
    /// evaluated at points constructed inside the simplex — never by
    /// solving unconstrained and then clipping negatives / renormalizing.
    /// </summary>
    public static DerivationResult Derive(
        SampleData sample,
        EndmemberSetData endmembers,
        FractionationData fractionation,
        PriorData prior,
        double chi2Scale = 1.0)
    {
        var diagnostics = new List<Diagnostic>();
        int k = endmembers.Members.Count;
        if (k < 2 || k > 3)
            throw new InvalidOperationException("this tool supports 2 or 3 endmembers");

        // --- dimension reconciliation: missing isotope dims are dropped with diagnostics, never invented
        var sampleDims = sample.Dims;
        var endDims = endmembers.Members[0].Dims;
        var dims = sampleDims.Where(endDims.Contains).ToList();
        foreach (var d in sampleDims.Except(dims))
            diagnostics.Add(new Diagnostic("dim-unshared", $"sample dim '{d}' absent from endmembers; left raw, excluded from derivation", d));
        foreach (var d in endDims.Except(dims))
            diagnostics.Add(new Diagnostic("dim-missing-in-sample", $"endmember dim '{d}' missing in sample; projected out, diagnostic retained", d));
        if (dims.Count == 0)
            return Empty(DerivationStatus.NoFeasibleRegion, double.PositiveInfinity, false, dims, diagnostics, fractionation, endmembers);

        int d = dims.Count;
        var sMap = dims.Select(x => sampleDims.IndexOf(x)).ToArray();
        var eMap = dims.Select(x => endDims.IndexOf(x)).ToArray();

        // --- unit normalization happens only here, in the derived layer; raw records are untouched
        var s = new double[d];
        for (int i = 0; i < d; i++)
        {
            if (!UnitConvert.TryNormalize(sample.Values[sMap[i]], sample.Unit, dims[i], out s[i]))
            {
                diagnostics.Add(new Diagnostic("unit-unconvertible",
                    $"sample value for '{dims[i]}' in unit '{sample.Unit}' cannot be normalized; value left in raw record", dims[i]));
                return Empty(DerivationStatus.NoFeasibleRegion, double.PositiveInfinity, false, dims, diagnostics, fractionation, endmembers);
            }
        }
        var sigmaS = Project(sample.Covariance, sMap, d);

        // --- expand endmember states (a member may carry discrete alternative states -> union of feasible sets)
        var combos = new List<List<(double[] mean, double[,] cov)>>();
        foreach (var m in endmembers.Members)
        {
            var states = new List<(double[], double[,])>();
            if (m.States is { Count: > 0 })
            {
                foreach (var st in m.States)
                    states.Add((NormalizeVec(st.Mean, m.Unit, dims, eMap, m.Id, diagnostics), Project(st.Covariance, eMap, d)));
            }
            else
            {
                states.Add((NormalizeVec(m.Mean, m.Unit, dims, eMap, m.Id, diagnostics), Project(m.Covariance, eMap, d)));
            }
            combos.Add(states);
        }
        if (diagnostics.Any(x => x.Code == "unit-unconvertible"))
            return Empty(DerivationStatus.NoFeasibleRegion, double.PositiveInfinity, false, dims, diagnostics, fractionation, endmembers);

        var comboList = Carthesian(combos);
        if (comboList.Count > 64) comboList = comboList.Take(64).ToList();

        double threshold = Chi2_95[System.Math.Min(d, 4)] * chi2Scale;
        double step = 0.02;
        var d2map = new Dictionary<(int, int), double>();
        double worstCond = 0;

        foreach (var combo in comboList)
        {
            var a = new double[d, k];
            var sigma = (double[,])sigmaS.Clone();
            for (int j = 0; j < k; j++)
            {
                var m = endmembers.Members[j];
                if (!fractionation.OffsetsPerEndmember.TryGetValue(m.Id, out var offset))
                {
                    offset = new double[endDims.Count].ToList();
                    diagnostics.Add(new Diagnostic("fractionation-missing",
                        $"no fractionation offset for endmember '{m.Id}' in version '{fractionation.Version}'; zero assumed and flagged", null));
                }
                for (int i = 0; i < d; i++)
                    a[i, j] = combo[j].mean[i] + offset[eMap[i]];
                for (int r = 0; r < d; r++)
                    for (int c = 0; c < d; c++)
                        sigma[r, c] += combo[j].cov[r, c] / k; // correlated errors propagate through full covariance
            }
            var sv = LinearAlgebra.SingularValues(a);
            double cond = sv[0] <= 1e-12 ? double.PositiveInfinity : sv[^1] / sv[0];
            worstCond = System.Math.Max(worstCond, cond);

            GridSimplex(k, step, prior, f =>
            {
                var r = Residual(s, a, f, d, k);
                double d2 = LinearAlgebra.Mahalanobis2(r, sigma);
                if (d2 <= threshold)
                {
                    var key = Key(f, step);
                    if (!d2map.TryGetValue(key, out double old) || d2 < old)
                        d2map[key] = d2;
                }
            });
        }

        bool identifiable = worstCond < 1.0 / CollinearityTolerance && k <= d + 1;
        if (!identifiable)
            diagnostics.Add(new Diagnostic("identifiability-insufficient",
                $"endmember matrix near-collinear (condition number {worstCond:F1}); proportions not uniquely identifiable — full feasible candidate set retained", null));

        double loSum = prior.Lower.Take(k).Sum();
        double hiSum = prior.Upper.Take(k).Sum();
        if (loSum > 1.0 + 1e-9 || hiSum < 1.0 - 1e-9)
        {
            diagnostics.Add(new Diagnostic("infeasible-prior",
                $"prior bounds infeasible on the simplex: sum(lower)={loSum:F3}, sum(upper)={hiSum:F3}", null));
            return Empty(DerivationStatus.InfeasiblePrior, worstCond, identifiable, dims, diagnostics, fractionation, endmembers);
        }

        // representative matrix (first combo) for envelope test / residuals
        var a0 = new double[d, k];
        var combo0 = comboList[0];
        for (int j = 0; j < k; j++)
        {
            var off = fractionation.OffsetsPerEndmember.TryGetValue(endmembers.Members[j].Id, out var o) ? o : new double[endDims.Count].ToList();
            for (int i = 0; i < d; i++) a0[i, j] = combo0[j].mean[i] + off[eMap[i]];
        }
        bool outside = OutsideEnvelope(s, a0, d, k);
        if (outside)
            diagnostics.Add(new Diagnostic("sample-outside-envelope",
                "sample lies outside the endmember convex hull; feasible region may be empty or boundary-hugging", null));

        var regions = BuildRegions(d2map, step, s, a0, d, k);
        string status = !identifiable ? DerivationStatus.IdentifiabilityInsufficient
            : regions.Count == 0 && outside ? DerivationStatus.SampleOutsideEnvelope
            : regions.Count == 0 ? DerivationStatus.NoFeasibleRegion
            : outside ? DerivationStatus.SampleOutsideEnvelope
            : DerivationStatus.Ok;
        return new DerivationResult(status, worstCond, identifiable, dims, regions, diagnostics,
            fractionation.Version, endmembers.Version);
    }

    private static DerivationResult Empty(string status, double cond, bool ident, List<string> dims,
        List<Diagnostic> diags, FractionationData fr, EndmemberSetData em) =>
        new(status, cond, ident, dims, new(), diags, fr.Version, em.Version);

    private static double[] NormalizeVec(List<double> values, string unit, List<string> dims, int[] eMap,
        string owner, List<Diagnostic> diagnostics)
    {
        var v = new double[dims.Count];
        for (int i = 0; i < dims.Count; i++)
        {
            if (!UnitConvert.TryNormalize(values[eMap[i]], unit, dims[i], out v[i]))
                diagnostics.Add(new Diagnostic("unit-unconvertible",
                    $"endmember '{owner}' dim '{dims[i]}' unit '{unit}' unconvertible; raw value preserved", dims[i]));
        }
        return v;
    }

    private static List<List<T>> Carthesian<T>(List<List<T>> pools)
    {
        var acc = new List<List<T>> { new() };
        foreach (var pool in pools)
            acc = acc.SelectMany(a => pool.Select(x => a.Concat(new[] { x }).ToList())).ToList();
        return acc;
    }

    private static (int, int) Key(double[] f, double step) =>
        ((int)System.Math.Round(f[0] / step), (int)System.Math.Round(f[1] / step));

    private static void GridSimplex(int k, double step, PriorData prior, Action<double[]> visit)
    {
        int n = (int)System.Math.Round(1.0 / step);
        var f = new double[k];
        var lo = new int[k];
        var hi = new int[k];
        for (int j = 0; j < k; j++)
        {
            lo[j] = (int)System.Math.Ceiling((prior.Lower.Count > j ? prior.Lower[j] : 0) / step - 1e-9);
            hi[j] = (int)System.Math.Floor((prior.Upper.Count > j ? prior.Upper[j] : 1) / step + 1e-9);
        }
        void Rec(int j, int remaining)
        {
            if (j == k - 1)
            {
                if (remaining >= lo[j] && remaining <= hi[j])
                {
                    f[j] = remaining * step;
                    visit((double[])f.Clone());
                }
                return;
            }
            for (int v = lo[j]; v <= System.Math.Min(hi[j], remaining); v++)
            {
                f[j] = v * step;
                Rec(j + 1, remaining - v);
            }
        }
        Rec(0, n);
    }

    private static List<FeasibleRegion> BuildRegions(
        Dictionary<(int, int), double> d2map, double step, double[] s, double[,] a, int d, int k)
    {
        // connected components on the grid: distinct feasible regions are preserved, never merged
        var keySet = new HashSet<(int, int)>(d2map.Keys);
        var seen = new HashSet<(int, int)>();
        var regions = new List<FeasibleRegion>();
        int idx = 0;
        foreach (var seed in d2map.Keys)
        {
            if (seen.Contains(seed)) continue;
            var comp = new List<(int, int)>();
            var queue = new Queue<(int, int)>();
            queue.Enqueue(seed);
            seen.Add(seed);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                comp.Add(cur);
                (int, int)[] nbs = { (cur.Item1 + 1, cur.Item2), (cur.Item1 - 1, cur.Item2), (cur.Item1, cur.Item2 + 1), (cur.Item1, cur.Item2 - 1) };
                foreach (var nb in nbs)
                    if (keySet.Contains(nb) && seen.Add(nb))
                        queue.Enqueue(nb);
            }
            var pts = comp.Select(c =>
            {
                var f = new double[k];
                f[0] = c.Item1 * step;
                f[1] = c.Item2 * step;
                if (k > 2) f[2] = 1 - f[0] - f[1];
                return f;
            }).ToList();
            var best = comp.OrderBy(c => d2map[c]).First();
            var rep = new double[k];
            rep[0] = best.Item1 * step;
            rep[1] = best.Item2 * step;
            if (k > 2) rep[2] = 1 - rep[0] - rep[1];
            regions.Add(new FeasibleRegion(idx++, pts, rep, d2map[best], Residual(s, a, rep, d, k)));
        }
        return regions;
    }

    private static double[] Residual(double[] s, double[,] a, double[] f, int d, int k)
    {
        var r = new double[d];
        for (int i = 0; i < d; i++)
        {
            double pred = 0;
            for (int j = 0; j < k; j++) pred += a[i, j] * f[j];
            r[i] = s[i] - pred;
        }
        return r;
    }

    private static bool OutsideEnvelope(double[] s, double[,] a, int d, int k)
    {
        var ata = new double[k, k];
        var atb = new double[k];
        for (int i = 0; i < k; i++)
        {
            for (int j = 0; j < k; j++)
                for (int r = 0; r < d; r++)
                    ata[i, j] += a[r, i] * a[r, j];
            for (int r = 0; r < d; r++) atb[i] += a[r, i] * s[r];
        }
        double[] coords;
        try { coords = LinearAlgebra.Solve(ata, atb); }
        catch (InvalidOperationException) { return true; }
        double sum = coords.Sum();
        return coords.Any(c => c < -0.05) || sum < 0.9 || sum > 1.1;
    }

    private static double[,] Project(List<List<double>> cov, int[] map, int d)
    {
        var m = new double[d, d];
        for (int r = 0; r < d; r++)
            for (int c = 0; c < d; c++)
                m[r, c] = cov[map[r]][map[c]];
        return m;
    }
}

/// <summary>Unit normalization into the canonical per-dimension basis. Unknown conversions refuse to convert.</summary>
public static class UnitConvert
{
    public static bool TryNormalize(double value, string unit, string dim, out double normalized)
    {
        normalized = value;
        switch (unit)
        {
            case "permil":
            case "permil VPDB":
            case "permil AIR":
                return true; // canonical basis for delta values
            case "fraction":
                normalized = value * 1000.0;
                return true;
            default:
                return false;
        }
    }
}

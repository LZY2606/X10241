namespace IsotopeMix.App.Domain;

/// <summary>An isotope dimension with unit and coordinate basis (e.g. permil vs VPDB).</summary>
public sealed record IsotopeDim(string Id, string Unit, string Basis);

/// <summary>One sub-population component of an endmember distribution.</summary>
public sealed record EndmemberComponent(double[] Means, double[,] Cov);

public sealed record EndmemberDef(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<EndmemberComponent> Components);

public sealed record SampleDef(
    string Id,
    string Version,
    /// <summary>Values per dimension; null when the dimension is missing for this sample.</summary>
    double?[] Values,
    /// <summary>Full covariance over the *available* dimensions (order = dims with values).</summary>
    double[,] Cov);

public sealed record FractionationDef(
    string Version,
    /// <summary>Factors per endmember per dimension: [endmember][dim].</summary>
    double[][] Factors);

public sealed record PriorDef(double[] Lower, double[] Upper, string Description);

/// <summary>A machine-readable judgment emitted by the derivation rules.</summary>
public sealed record Judgment(
    string Code,
    string Severity,
    string Basis,
    IReadOnlyDictionary<string, object?> Detail);

public sealed record CandidatePoint(string Id, double[] Proportions, double Mahalanobis, int Region);

public sealed record AnalysisResult(
    bool IdentifiabilitySufficient,
    double ConditionNumber,
    int AvailableDimensions,
    int RegionCount,
    double MinMahalanobis,
    double ChiSquareThreshold,
    IReadOnlyList<string> Flags,
    IReadOnlyList<Judgment> Judgments,
    IReadOnlyList<CandidatePoint> Candidates,
    IReadOnlyList<double[]> EffectiveMeans,
    double[] SampleAdjustedReference,
    bool PriorInfeasible);

public static class MixingModel
{
    public const string RuleVersion = "mixrule-1.0";
    public const double CollinearityThreshold = 25.0;
    public const int GridSteps = 48;

    public static double ChiSquare95(int df) => df switch
    {
        1 => 3.841,
        2 => 5.991,
        3 => 7.815,
        4 => 9.488,
        _ => df + 2.0 * Math.Sqrt(2.0 * df),
    };

    /// <summary>
    /// Honest simplex check: proportions must satisfy sum-to-one and non-negativity
    /// as given. Out-of-simplex points are rejected, never clipped and renormalized.
    /// </summary>
    public static bool IsOnSimplex(double[] p, double tol = 1e-9)
    {
        double sum = 0;
        foreach (var v in p)
        {
            if (v < -tol) return false;
            sum += v;
        }
        return Math.Abs(sum - 1.0) <= 1e-6;
    }

    /// <summary>Effective endmember mean after fractionation: m + Delta.</summary>
    public static double[] EffectiveMean(EndmemberComponent c, double[] delta) =>
        c.Means.Select((m, d) => m + delta[d]).ToArray();

    /// <summary>
    /// Feasibility of one proportion vector: residual between sample and the mixture,
    /// with correlated errors propagated through the full covariance
    /// C(p) = C_sample + sum_i p_i^2 * C_endmember_i.
    /// Endmember component heterogeneity is handled by taking the best component
    /// combination (a union, which can yield disconnected feasible regions).
    /// </summary>
    public static double FeasibilityAt(
        double[] p,
        IReadOnlyList<EndmemberDef> endmembers,
        FractionationDef frac,
        SampleDef sample,
        int[] availDims)
    {
        var df = availDims.Length;
        var best = double.PositiveInfinity;
        var k = endmembers.Count;
        var comboCount = 1;
        foreach (var e in endmembers) comboCount *= e.Components.Count;
        for (var combo = 0; combo < comboCount; combo++)
        {
            var idx = combo;
            var mix = new double[df];
            var cov = new double[df, df];
            for (var a = 0; a < df; a++)
            for (var b = 0; b < df; b++)
                cov[a, b] = sample.Cov[a, b];
            for (var i = 0; i < k; i++)
            {
                var comp = endmembers[i].Components[idx % endmembers[i].Components.Count];
                idx /= endmembers[i].Components.Count;
                for (var a = 0; a < df; a++)
                {
                    var d = availDims[a];
                    mix[a] += p[i] * (comp.Means[d] + frac.Factors[i][d]);
                }
                for (var a = 0; a < df; a++)
                for (var b = 0; b < df; b++)
                    cov[a, b] += p[i] * p[i] * comp.Cov[availDims[a], availDims[b]];
            }
            var r = new double[df];
            for (var a = 0; a < df; a++) r[a] = sample.Values[availDims[a]]!.Value - mix[a];
            double d2;
            try
            {
                d2 = LinAlg.Mahalanobis2(r, cov);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            if (d2 < best) best = d2;
        }
        return best;
    }

    public static AnalysisResult Analyze(
        IReadOnlyList<IsotopeDim> dims,
        IReadOnlyList<EndmemberDef> endmembers,
        FractionationDef frac,
        SampleDef sample,
        PriorDef? prior)
    {
        var judgments = new List<Judgment>();
        var flags = new List<string>();
        var k = endmembers.Count;

        var availDims = Enumerable.Range(0, dims.Count)
            .Where(d => sample.Values[d].HasValue)
            .ToArray();
        if (availDims.Length < dims.Count)
        {
            flags.Add("missing_dimension");
            var missing = Enumerable.Range(0, dims.Count).Where(d => !sample.Values[d].HasValue)
                .Select(d => dims[d].Id).ToArray();
            judgments.Add(new Judgment(
                "dimension.missing",
                "warning",
                "Sample lacks isotope dimensions; analysis proceeds on the available subspace only.",
                new Dictionary<string, object?> { ["missing"] = missing, ["available_df"] = availDims.Length }));
        }

        // Identifiability: condition number of the centered effective endmember matrix.
        var primMeans = endmembers
            .Select((e, i) => EffectiveMean(e.Components[0], frac.Factors[i]))
            .ToList();
        var m = new double[dims.Count, k];
        for (var d = 0; d < dims.Count; d++)
        {
            double mean = 0;
            for (var i = 0; i < k; i++) mean += primMeans[i][d];
            mean /= k;
            for (var i = 0; i < k; i++) m[d, i] = primMeans[i][d] - mean;
        }
        var cond = LinAlg.ConditionNumber(m);
        var identifiable = cond < CollinearityThreshold && availDims.Length >= Math.Min(k - 1, dims.Count);
        if (!identifiable)
        {
            flags.Add("near_collinear");
            judgments.Add(new Judgment(
                "identifiability.insufficient",
                "error",
                "Endmembers are near-collinear in isotope space; mixing proportions are not uniquely " +
                "identifiable. The full feasible band is reported instead of a point estimate.",
                new Dictionary<string, object?> { ["condition_number"] = Math.Round(cond, 3) }));
        }

        var correlated = HasOffDiagonal(sample.Cov) ||
                         endmembers.SelectMany(e => e.Components).Any(c => HasOffDiagonal(c.Cov));
        if (correlated)
        {
            flags.Add("correlated_errors");
            judgments.Add(new Judgment(
                "error.correlated.propagated",
                "info",
                "Measurement errors are correlated; residuals are evaluated with the full covariance " +
                "C(p) = C_sample + sum p_i^2 C_i (Mahalanobis), not a diagonal approximation.",
                new Dictionary<string, object?>()));
        }

        judgments.Add(new Judgment(
            "simplex.exact",
            "info",
            "Proportion constraints (sum=1, p_i>=0) are enforced exactly on the simplex; " +
            "infeasible points are rejected, never truncated and renormalized.",
            new Dictionary<string, object?> { ["normalization"] = "none" }));

        // Grid the simplex; evaluate feasibility honestly.
        var threshold = ChiSquare95(availDims.Length);
        var candidates = new List<CandidatePoint>();
        var feasibleGrid = new Dictionary<(int, int, int), double>();
        var minD2 = double.PositiveInfinity;
        var n = GridSteps;
        for (var a = 0; a <= n; a++)
        for (var b = 0; b <= n - a; b++)
        {
            var c = n - a - b;
            if (k == 3)
            {
                var p = new[] { a / (double)n, b / (double)n, c / (double)n };
                if (prior is not null && !SatisfiesPrior(p, prior)) continue;
                var d2 = FeasibilityAt(p, endmembers, frac, sample, availDims);
                if (d2 < minD2) minD2 = d2;
                if (d2 <= threshold)
                {
                    feasibleGrid[(a, b, c)] = d2;
                    candidates.Add(new CandidatePoint($"cand_{a}_{b}_{c}", p, Math.Round(d2, 4), -1));
                }
            }
        }
        if (k != 3)
        {
            // Generic simplex sampler for k != 3 (coarser).
            foreach (var p in SimplexSamples(k, 14))
            {
                if (prior is not null && !SatisfiesPrior(p, prior)) continue;
                var d2 = FeasibilityAt(p, endmembers, frac, sample, availDims);
                if (d2 < minD2) minD2 = d2;
                if (d2 <= threshold)
                    candidates.Add(new CandidatePoint(
                        $"cand_{candidates.Count}", p, Math.Round(d2, 4), -1));
            }
        }

        // Connected components on the triangular grid -> distinct feasible regions.
        var regionCount = 0;
        if (k == 3)
        {
            var assigned = new Dictionary<(int, int, int), int>();
            foreach (var key in feasibleGrid.Keys)
            {
                if (assigned.ContainsKey(key)) continue;
                regionCount++;
                var stack = new Stack<(int, int, int)>();
                stack.Push(key);
                assigned[key] = regionCount;
                while (stack.Count > 0)
                {
                    var (a, b, c) = stack.Pop();
                    foreach (var nb in Neighbors(a, b, c))
                    {
                        if (!feasibleGrid.ContainsKey(nb) || assigned.ContainsKey(nb)) continue;
                        assigned[nb] = regionCount;
                        stack.Push(nb);
                    }
                }
            }
            for (var i = 0; i < candidates.Count; i++)
            {
                var parts = candidates[i].Id.Split('_');
                var key = (int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                candidates[i] = candidates[i] with { Region = assigned[key] };
            }
        }
        else if (candidates.Count > 0)
        {
            regionCount = 1;
            for (var i = 0; i < candidates.Count; i++)
                candidates[i] = candidates[i] with { Region = 1 };
        }

        if (regionCount > 1)
        {
            flags.Add("multiple_regions");
            judgments.Add(new Judgment(
                "region.multiple",
                "warning",
                "The feasible proportion set has multiple disconnected regions; all are kept as " +
                "candidates because the data cannot choose between them.",
                new Dictionary<string, object?> { ["regions"] = regionCount }));
        }

        var outsideEnvelope = candidates.Count == 0;
        if (outsideEnvelope)
        {
            flags.Add("outside_envelope");
            judgments.Add(new Judgment(
                "envelope.outside",
                "error",
                "No proportion vector on the simplex reconciles the sample with the endmember " +
                "envelope at the 95% covariance-propagated tolerance. The empty region is reported " +
                "as-is; no point estimate is fabricated.",
                new Dictionary<string, object?> { ["min_mahalanobis"] = Math.Round(minD2, 3) }));
        }

        var priorInfeasible = false;
        if (prior is not null)
        {
            var withoutPrior = CountFeasibleWithoutPrior(dims, endmembers, frac, sample, availDims, threshold, k);
            if (withoutPrior > 0 && candidates.Count == 0)
            {
                priorInfeasible = true;
                flags.Add("prior_infeasible");
                judgments.Add(new Judgment(
                    "prior.infeasible",
                    "error",
                    "The prior box does not intersect the data-feasible region. Both the prior and " +
                    "the unconstrained feasible set are preserved; the prior is not silently relaxed.",
                    new Dictionary<string, object?>
                    {
                        ["prior"] = prior.Description,
                        ["feasible_without_prior"] = withoutPrior,
                    }));
            }
        }

        return new AnalysisResult(
            identifiable,
            Math.Round(cond, 3),
            availDims.Length,
            regionCount,
            double.IsPositiveInfinity(minD2) ? -1 : Math.Round(minD2, 4),
            threshold,
            flags,
            judgments,
            candidates,
            primMeans,
            availDims.Select(d => sample.Values[d]!.Value).ToArray(),
            priorInfeasible);
    }

    private static int CountFeasibleWithoutPrior(
        IReadOnlyList<IsotopeDim> dims,
        IReadOnlyList<EndmemberDef> endmembers,
        FractionationDef frac,
        SampleDef sample,
        int[] availDims,
        double threshold,
        int k)
    {
        var count = 0;
        if (k == 3)
        {
            var n = GridSteps;
            for (var a = 0; a <= n; a++)
            for (var b = 0; b <= n - a; b++)
            {
                var p = new[] { a / (double)n, b / (double)n, (n - a - b) / (double)n };
                if (FeasibilityAt(p, endmembers, frac, sample, availDims) <= threshold) count++;
            }
        }
        else
        {
            foreach (var p in SimplexSamples(k, 14))
                if (FeasibilityAt(p, endmembers, frac, sample, availDims) <= threshold) count++;
        }
        return count;
    }

    private static IEnumerable<(int, int, int)> Neighbors(int a, int b, int c)
    {
        yield return (a + 1, b - 1, c);
        yield return (a - 1, b + 1, c);
        yield return (a + 1, b, c - 1);
        yield return (a - 1, b, c + 1);
        yield return (a, b + 1, c - 1);
        yield return (a, b - 1, c + 1);
    }

    private static IEnumerable<double[]> SimplexSamples(int k, int n)
    {
        var p = new double[k];
        var counts = new int[k];
        foreach (var combo in Compositions(n, k))
        {
            for (var i = 0; i < k; i++) p[i] = combo[i] / (double)n;
            yield return p;
        }
    }

    private static IEnumerable<int[]> Compositions(int total, int parts)
    {
        if (parts == 1)
        {
            yield return new[] { total };
            yield break;
        }
        for (var i = 0; i <= total; i++)
        foreach (var rest in Compositions(total - i, parts - 1))
            yield return new[] { i }.Concat(rest).ToArray();
    }

    private static bool SatisfiesPrior(double[] p, PriorDef prior)
    {
        for (var i = 0; i < p.Length; i++)
            if (p[i] < prior.Lower[i] - 1e-12 || p[i] > prior.Upper[i] + 1e-12)
                return false;
        return true;
    }

    private static bool HasOffDiagonal(double[,] cov)
    {
        for (var i = 0; i < cov.GetLength(0); i++)
        for (var j = 0; j < cov.GetLength(1); j++)
            if (i != j && Math.Abs(cov[i, j]) > 1e-12)
                return true;
        return false;
    }
}

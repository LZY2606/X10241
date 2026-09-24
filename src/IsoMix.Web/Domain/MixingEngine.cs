namespace IsoMix.Web.Domain;

/// <summary>
/// Feasible-region mixing engine. Searches the proportion simplex directly on a grid:
/// the sum-to-one and non-negativity constraints are native to the search space,
/// never faked by solving unconstrained and renormalizing afterwards.
/// Correlated measurement error is propagated through the full covariance matrix.
/// </summary>
public static class MixingEngine
{
    public const int DefaultResolution = 24;
    public const double CollinearityTolerance = 1e-4;

    /// <summary>All integer compositions of k parts summing to resolution, in deterministic lexicographic order.</summary>
    public static List<int[]> SimplexGrid(int k, int resolution)
    {
        var grid = new List<int[]>();
        var current = new int[k];
        void Rec(int idx, int remaining)
        {
            if (idx == k - 1) { current[idx] = remaining; grid.Add((int[])current.Clone()); return; }
            for (int v = 0; v <= remaining; v++) { current[idx] = v; Rec(idx + 1, remaining - v); }
        }
        Rec(0, resolution);
        return grid;
    }

    public static DerivationResult Derive(
        NormSample sample,
        IReadOnlyList<NormEndmember> endmembers,
        NormCovariance covariance,
        NormFractionation fractionation,
        NormPrior? prior,
        int resolution = DefaultResolution)
    {
        var flags = new List<string>();
        var diagnostics = new List<Diagnostic>();
        int k = endmembers.Count;

        // Derived layer works on the dims shared by sample and covariance; missing dims are flagged, never invented.
        var dims = covariance.Dims.Where(sample.Values.ContainsKey).ToArray();
        foreach (var missing in covariance.Dims.Where(d => !sample.Values.ContainsKey(d)))
        {
            if (!flags.Contains(DerivationResult.FlagMissingDim)) flags.Add(DerivationResult.FlagMissingDim);
            diagnostics.Add(new Diagnostic("missing-dim",
                $"Sample {sample.SourceId} lacks isotope dimension {missing}; derivation proceeds on shared dims only, raw value stays untouched.", sample.SourceId));
        }
        if (dims.Length == 0)
            return new DerivationResult(DerivationResult.Infeasible, flags, new(), null,
                Array.Empty<double>(), Array.Empty<string>(), double.PositiveInfinity,
                double.PositiveInfinity, resolution, diagnostics, Array.Empty<bool>(), Array.Empty<int>());

        // Fractionation-adjusted endmember means.
        var adjusted = endmembers.Select(em => dims.Select(d =>
            (em.Mean.TryGetValue(d, out var v) ? v : double.NaN)
            + (fractionation.OffsetPerDim.TryGetValue(d, out var off) ? off : 0.0)).ToArray()).ToArray();

        // Collinearity: eigenvalues of the scatter of adjusted endmember means.
        var meanVec = new double[dims.Length];
        foreach (var m in adjusted) for (int d = 0; d < dims.Length; d++) meanVec[d] += m[d] / k;
        var scatter = new double[k][];
        for (int i = 0; i < k; i++) scatter[i] = new double[k];
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++)
                for (int d = 0; d < dims.Length; d++)
                    scatter[i][j] += (adjusted[i][d] - meanVec[d]) * (adjusted[j][d] - meanVec[d]) / k;
        var eigen = LinAlg.EigenvaluesSymmetric(scatter);
        double lambdaMax = eigen[^1];
        double lambdaMin = Math.Max(eigen[0], 0.0);
        double condition = lambdaMin <= 0 ? double.PositiveInfinity : lambdaMax / lambdaMin;
        bool nearCollinear = double.IsInfinity(condition) || condition > 1.0 / CollinearityTolerance;
        if (nearCollinear)
        {
            flags.Add(DerivationResult.FlagNearCollinear);
            diagnostics.Add(new Diagnostic("near-collinear",
                $"Endmember scatter condition number {condition:E2} exceeds 1/{CollinearityTolerance:E0}; mixing proportions are not uniquely identifiable.", null));
        }

        // Correlated error propagation uses the full covariance, restricted to shared dims.
        var dimIndex = dims.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var sigma = new double[dims.Length][];
        for (int i = 0; i < dims.Length; i++)
        {
            sigma[i] = new double[dims.Length];
            int ci = Array.IndexOf(covariance.Dims, dims[i]);
            for (int j = 0; j < dims.Length; j++)
                sigma[i][j] = covariance.Matrix[ci][Array.IndexOf(covariance.Dims, dims[j])];
        }
        bool correlated = false;
        for (int i = 0; i < dims.Length; i++)
            for (int j = 0; j < dims.Length; j++)
                if (i != j && Math.Abs(sigma[i][j]) > 1e-12) correlated = true;
        if (correlated) flags.Add(DerivationResult.FlagCorrelatedErrors);
        var sigmaInv = LinAlg.Invert(sigma);
        double threshold = LinAlg.ChiSquare95(dims.Length);

        var obs = dims.Select(d => sample.Values[d]).ToArray();
        var grid = SimplexGrid(k, resolution);
        var feasible = new bool[grid.Count];
        var d2values = new double[grid.Count];

        // Prior feasibility is checked as a constraint on the simplex, not by clipping.
        var priorMin = new double[k];
        if (prior != null)
            for (int i = 0; i < k; i++)
                if (prior.MinProportion.TryGetValue(endmembers[i].SourceId, out var lo)) priorMin[i] = lo;
        bool priorSelfContradictory = priorMin.Sum() > 1.0 + 1e-9;

        double minD2 = double.PositiveInfinity;
        int bestIdx = -1;
        for (int gi = 0; gi < grid.Count; gi++)
        {
            var f = grid[gi].Select(v => (double)v / resolution).ToArray();
            var pred = new double[dims.Length];
            for (int i = 0; i < k; i++)
                for (int d = 0; d < dims.Length; d++)
                    pred[d] += f[i] * adjusted[i][d];
            var r = obs.Zip(pred, (o, p) => o - p).ToArray();
            double d2 = LinAlg.Mahalanobis2(r, sigmaInv);
            d2values[gi] = d2;
            if (d2 < minD2) { minD2 = d2; bestIdx = gi; }
            if (d2 > threshold) continue;
            bool priorOk = !priorSelfContradictory;
            for (int i = 0; i < k && priorOk; i++)
                if (f[i] < priorMin[i] - 1e-9) priorOk = false;
            feasible[gi] = priorOk;
        }

        // Connected components of feasible cells -> candidate regions (all legal candidates are kept).
        var regionOf = new int[grid.Count];
        Array.Fill(regionOf, -1);
        int regionCount = 0;
        var indexOf = new Dictionary<string, int>(grid.Count);
        for (int gi = 0; gi < grid.Count; gi++) indexOf[string.Join(',', grid[gi])] = gi;
        for (int gi = 0; gi < grid.Count; gi++)
        {
            if (!feasible[gi] || regionOf[gi] >= 0) continue;
            var stack = new Stack<int>();
            stack.Push(gi);
            regionOf[gi] = regionCount;
            while (stack.Count > 0)
            {
                var cur = grid[stack.Pop()];
                for (int a = 0; a < k; a++)
                for (int b = 0; b < k; b++)
                {
                    if (a == b || cur[a] == 0) continue;
                    var nb = (int[])cur.Clone();
                    nb[a]--; nb[b]++;
                    if (indexOf.TryGetValue(string.Join(',', nb), out int ni) && feasible[ni] && regionOf[ni] < 0)
                    {
                        regionOf[ni] = regionCount;
                        stack.Push(ni);
                    }
                }
            }
            regionCount++;
        }

        var candidates = new List<CandidateRegion>();
        for (int region = 0; region < regionCount; region++)
        {
            var centroid = new double[k];
            int count = 0;
            double regionMin = double.PositiveInfinity;
            for (int gi = 0; gi < grid.Count; gi++)
            {
                if (regionOf[gi] != region) continue;
                count++;
                regionMin = Math.Min(regionMin, d2values[gi]);
                for (int i = 0; i < k; i++) centroid[i] += (double)grid[gi][i] / resolution;
            }
            for (int i = 0; i < k; i++) centroid[i] /= count;
            candidates.Add(new CandidateRegion(region, centroid, (double)count / grid.Count, regionMin));
        }

        if (candidates.Count > 1) flags.Add(DerivationResult.FlagMultipleRegions);

        bool anyFeasibleWithoutPrior = false;
        if (prior != null && candidates.Count == 0)
        {
            for (int gi = 0; gi < grid.Count; gi++)
                if (d2values[gi] <= threshold) { anyFeasibleWithoutPrior = true; break; }
        }
        if (priorSelfContradictory || (prior != null && candidates.Count == 0 && anyFeasibleWithoutPrior))
        {
            flags.Add(DerivationResult.FlagInfeasiblePrior);
            diagnostics.Add(new Diagnostic("infeasible-prior",
                priorSelfContradictory
                    ? $"Prior minimum proportions sum to {priorMin.Sum():F3} > 1; no composition can satisfy it."
                    : "Prior constraints exclude every statistically feasible composition.", prior?.SourceId));
        }

        if (candidates.Count == 0 && !flags.Contains(DerivationResult.FlagInfeasiblePrior))
        {
            flags.Add(DerivationResult.FlagOutsideEnvelope);
            diagnostics.Add(new Diagnostic("outside-envelope",
                $"Sample lies outside the endmember mixing envelope; closest composition has d2={minD2:F2} above the 95% threshold {threshold:F2}.", sample.SourceId));
        }

        var bestF = bestIdx >= 0 ? grid[bestIdx].Select(v => (double)v / resolution).ToArray() : new double[k];
        var bestPred = new double[dims.Length];
        for (int i = 0; i < k; i++)
            for (int d = 0; d < dims.Length; d++)
                bestPred[d] += bestF[i] * adjusted[i][d];
        var residuals = obs.Zip(bestPred, (o, p) => o - p).ToArray();

        string status = candidates.Count == 0
            ? DerivationResult.Infeasible
            : nearCollinear || candidates.Count > 1
                ? DerivationResult.IdentifiabilityInsufficient
                : DerivationResult.Ok;

        // A single point answer is only exposed when the problem is genuinely identifiable.
        double[]? best = status == DerivationResult.Ok ? bestF : null;

        return new DerivationResult(status, flags, candidates, best, residuals, dims,
            minD2, condition, resolution, diagnostics, feasible, regionOf);
    }
}

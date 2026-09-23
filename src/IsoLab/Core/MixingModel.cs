namespace IsoLab.Core;

public sealed record EndmemberInput(string Id, string Name, double[] Values);

public sealed record MixingInput(
    string[] SampleDims,
    double[] SampleValues,
    string[] EndmemberDims,
    EndmemberInput[] Endmembers,
    double[][] Covariance,           // full covariance on EndmemberDims (correlated errors)
    double[][] FractionationOffsets, // per endmember, on EndmemberDims
    double[] PriorMin,               // per endmember lower bound from prior assumptions
    double ConditionThreshold = 100.0,
    int GridDivisions = 40);

public sealed record FeasibleRegion(
    int Component,
    double[] Centroid,
    double[] Min,
    double[] Max,
    double MeanResidual,
    int GridPoints);

public sealed record MixingResult(
    string Status, // ok | identifiability-insufficient | infeasible-prior | empty-feasible-region
    IReadOnlyList<string> Diagnostics,
    string[] UsedDims,
    string[] MissingDims,
    double ConditionNumber,
    bool InsideEnvelope,
    double[]? UnconstrainedEstimate, // equality-only LS; never truncated / renormalized
    IReadOnlyList<FeasibleRegion> Regions,
    double Chi2Threshold);

/// <summary>
/// Stable-isotope mixing-model core. Produces feasible proportion regions on the
/// simplex; never collapses the answer to a single point and never fakes simplex
/// membership by truncating and renormalizing an unconstrained estimate.
/// </summary>
public static class MixingModel
{
    private static readonly double[] Chi2_95 = { 3.841, 5.991, 7.815, 9.488, 11.070, 12.592, 14.067, 15.507 };

    public static MixingResult Analyze(MixingInput input)
    {
        var diagnostics = new List<string>();
        var k = input.Endmembers.Length;

        // --- prior feasibility: sum of lower bounds must not exceed 1 ---
        var priorMin = input.PriorMin;
        if (priorMin.Sum() > 1.0 + 1e-9)
        {
            return new MixingResult("infeasible-prior",
                new[] { "infeasible-prior:lower-bounds-sum-gt-1" },
                Array.Empty<string>(), Array.Empty<string>(),
                double.NaN, false, null, Array.Empty<FeasibleRegion>(), double.NaN);
        }

        // --- dimension reconciliation: missing isotope dims stay diagnosed, not dropped silently ---
        var usedDims = input.EndmemberDims.Where(d => input.SampleDims.Contains(d)).ToArray();
        var missingDims = input.EndmemberDims.Where(d => !input.SampleDims.Contains(d)).ToArray();
        if (missingDims.Length > 0)
            diagnostics.Add("missing-isotope-dimension:" + string.Join(",", missingDims));
        if (usedDims.Length == 0)
            return new MixingResult("empty-feasible-region",
                diagnostics.Append("no-shared-isotope-dimension").ToArray(),
                usedDims, missingDims, double.NaN, false, null, Array.Empty<FeasibleRegion>(), double.NaN);

        var dimIndex = usedDims.Select(d => Array.IndexOf(input.EndmemberDims, d)).ToArray();
        var sampleIndex = usedDims.Select(d => Array.IndexOf(input.SampleDims, d)).ToArray();
        var d = usedDims.Length;

        // fractionation-adjusted endmembers, restricted to used dims: E'[dim][member]
        var e = new double[d][];
        for (var i = 0; i < d; i++)
        {
            e[i] = new double[k];
            for (var j = 0; j < k; j++)
                e[i][j] = input.Endmembers[j].Values[dimIndex[i]] + input.FractionationOffsets[j][dimIndex[i]];
        }
        var y = new double[d];
        for (var i = 0; i < d; i++) y[i] = input.SampleValues[sampleIndex[i]];

        // covariance restricted to used dims; correlated errors propagate through Σ⁻¹
        var cov = new double[d][];
        var correlated = false;
        for (var i = 0; i < d; i++)
        {
            cov[i] = new double[d];
            for (var j = 0; j < d; j++)
            {
                cov[i][j] = input.Covariance[dimIndex[i]][dimIndex[j]];
                if (i != j && Math.Abs(cov[i][j]) > 1e-15) correlated = true;
            }
        }
        if (correlated) diagnostics.Add("correlated-errors-propagated");
        var covInv = LinAlg.Inverse(cov);

        // --- identifiability: condition number of [E'; 1ᵀ] ---
        var aCond = new double[d + 1][];
        for (var i = 0; i < d; i++) aCond[i] = e[i];
        aCond[d] = Enumerable.Repeat(1.0, k).ToArray();
        var condition = LinAlg.ConditionNumber(aCond);
        var identifiable = condition <= input.ConditionThreshold;
        if (!identifiable) diagnostics.Add("endmembers-near-collinear");

        // --- unconstrained (equality-only) estimate: reported as-is, never clipped ---
        var unconstrained = LinAlg.EqualityConstrainedLeastSquares(e, y);
        var unconstrainedInfeasible = unconstrained.Any(p => p < -1e-9);
        if (unconstrainedInfeasible) diagnostics.Add("unconstrained-solution-infeasible:not-truncated-not-renormalized");

        var chi2 = Chi2_95[Math.Min(d, Chi2_95.Length) - 1];

        double Residual2(double[] p)
        {
            var r = new double[d];
            for (var i = 0; i < d; i++)
            {
                var s = y[i];
                for (var j = 0; j < k; j++) s -= e[i][j] * p[j];
                r[i] = s;
            }
            double q = 0;
            for (var i = 0; i < d; i++)
            for (var j = 0; j < d; j++)
                q += r[i] * covInv[i][j] * r[j];
            return q;
        }

        var unconstrainedResidual = Residual2(unconstrained);
        var insideEnvelope = !unconstrainedInfeasible && unconstrainedResidual <= chi2;
        if (!insideEnvelope) diagnostics.Add("sample-outside-endmember-envelope");

        // --- feasible region on the simplex by exact simplex grid (points satisfy Σp=1, p≥0 by construction) ---
        var divisions = input.GridDivisions;
        if (k >= 6) divisions = Math.Min(divisions, 16);
        else if (k == 5) divisions = Math.Min(divisions, 24);
        var compositions = Compositions(k, divisions).ToList();
        var feasible = new bool[compositions.Count];
        var residuals = new double[compositions.Count];
        for (var idx = 0; idx < compositions.Count; idx++)
        {
            var p = compositions[idx];
            var ok = true;
            for (var j = 0; j < k; j++)
                if (p[j] < priorMin[j] - 1e-12) { ok = false; break; }
            if (!ok) continue;
            var r2 = Residual2(p);
            residuals[idx] = r2;
            feasible[idx] = r2 <= chi2;
        }

        // connected components over grid adjacency -> possibly multiple feasible regions
        var comp = new int[compositions.Count];
        Array.Fill(comp, -1);
        var nComp = 0;
        var compOf = new Dictionary<string, int>();
        for (var i = 0; i < compositions.Count; i++) compOf[Key(compositions[i])] = i;
        for (var i = 0; i < compositions.Count; i++)
        {
            if (!feasible[i] || comp[i] >= 0) continue;
            var stack = new Stack<int>();
            stack.Push(i); comp[i] = nComp;
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                var c = compositions[cur];
                for (var u = 0; u < k; u++)
                for (var v = 0; v < k; v++)
                {
                    if (u == v || c[u] == 0) continue;
                    var nb = (double[])c.Clone();
                    nb[u] -= 1.0 / divisions; nb[v] += 1.0 / divisions;
                    if (!compOf.TryGetValue(Key(nb), out var ni)) continue;
                    if (feasible[ni] && comp[ni] < 0) { comp[ni] = nComp; stack.Push(ni); }
                }
            }
            nComp++;
        }
        if (nComp > 1) diagnostics.Add("multiple-feasible-regions:" + nComp);

        var regions = new List<FeasibleRegion>();
        for (var cIdx = 0; cIdx < nComp; cIdx++)
        {
            var members = Enumerable.Range(0, compositions.Count).Where(i => comp[i] == cIdx).ToArray();
            var centroid = new double[k]; var min = new double[k]; var max = new double[k];
            Array.Fill(min, double.MaxValue); Array.Fill(max, double.MinValue);
            double rSum = 0;
            foreach (var i in members)
            {
                rSum += residuals[i];
                for (var j = 0; j < k; j++)
                {
                    centroid[j] += compositions[i][j] / members.Length;
                    min[j] = Math.Min(min[j], compositions[i][j]);
                    max[j] = Math.Max(max[j], compositions[i][j]);
                }
            }
            regions.Add(new FeasibleRegion(cIdx, centroid, min, max, rSum / members.Length, members.Length));
        }

        var status = !identifiable ? "identifiability-insufficient"
            : regions.Count == 0 ? "empty-feasible-region"
            : "ok";
        return new MixingResult(status, diagnostics, usedDims, missingDims,
            condition, insideEnvelope, unconstrained, regions, chi2);
    }

    private static string Key(double[] p) => string.Join(",", p.Select(x => Math.Round(x, 9)));

    /// <summary>All p with p_i = n_i/divisions, n_i ≥ 0 integers, Σn_i = divisions. Exact simplex points.</summary>
    public static IEnumerable<double[]> Compositions(int k, int divisions)
    {
        var current = new int[k];
        current[0] = divisions;
        foreach (var c in CompositionsRec(current, 0, k, divisions))
            yield return c.Select(n => (double)n / divisions).ToArray();
    }

    private static IEnumerable<int[]> CompositionsRec(int[] current, int pos, int k, int remaining)
    {
        if (pos == k - 1)
        {
            current[pos] = remaining;
            yield return (int[])current.Clone();
            yield break;
        }
        for (var n = remaining; n >= 0; n--)
        {
            current[pos] = n;
            foreach (var c in CompositionsRec(current, pos + 1, k, remaining - n))
                yield return c;
        }
    }
}

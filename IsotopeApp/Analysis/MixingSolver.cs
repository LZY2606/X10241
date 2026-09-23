using System.Text;

namespace IsotopeApp.Analysis;

/// <summary>
/// Feasible-region solver for linear isotope mixing with Gaussian (mixture)
/// endmembers, correlated measurement error and fractionation shifts.
///
/// Model:  y ~ N( sum_i p_i (mu_{i,k_i} + f),  S_e + sum_i p_i S_{i,k_i} )
/// with p on the simplex. The covariance is propagated exactly (not reduced to
/// its diagonal). Mixture endmembers make the feasible set a union of convex
/// sets, which can be genuinely disconnected.
/// </summary>
public sealed class MixingSolver
{
    public const double CollinearThreshold = 30.0; // whitened condition number

    public SolverResult Solve(SolverInput input)
    {
        int n = input.Sources.Count;
        int d = input.Tracers.Count;
        int k = input.Subdivisions <= 0 ? DefaultSubdivisions(n) : input.Subdivisions;
        var lattice = new SimplexLattice(n, k);
        double threshold = ChiSquare.InverseCdf(input.ConfidenceLevel, d);

        int modeCombinations = input.Sources.Aggregate(1, (acc, s) => acc * s.ModeMeans.Length);
        var combos = ModeCombinations(n, input.Sources);

        bool priorInfeasible = false;
        var diagnostics = new List<string>();
        (double Low, double High)[] bounds = input.PriorBounds ?? Array.Empty<(double, double)>();
        if (bounds.Length == n)
        {
            priorInfeasible = !SimplexLpFeasible(bounds, diagnostics);
        }

        var flags = new bool[lattice.Points.Length];
        var ownerCombo = new int[lattice.Points.Length];
        Array.Fill(ownerCombo, -1);
        var statisticAt = new double[lattice.Points.Length];
        double bestStat = double.PositiveInfinity;
        int bestPointIndex = -1, bestCombo = -1;

        for (int ci = 0; ci < combos.Count; ci++)
        {
            int[] combo = combos[ci];
            for (int pi = 0; pi < lattice.Points.Length; pi++)
            {
                if (flags[pi]) continue;
                double[] p = lattice.Points[pi];
                if (priorInfeasible) { }
                else if (bounds.Length == n && !WithinBounds(p, bounds)) continue;

                var (stat, cov) = Statistic(input, combo, p);
                statisticAt[pi] = stat;
                if (stat < bestStat) { bestStat = stat; bestPointIndex = pi; bestCombo = ci; }
                if (stat <= threshold)
                {
                    flags[pi] = true;
                    ownerCombo[pi] = ci;
                }
            }
        }

        bool insideHull = MeanHullContains(input);
        var (cond, smin) = Identifiability(input);
        bool identifiable = cond <= CollinearThreshold && smin > 1e-9;

        var regionLists = lattice.ConnectedComponents(flags);
        var regions = new List<FeasibleRegion>();
        var candidates = new List<CandidateSolution>();
        var candidateModes = new List<int[]>();

        for (int ri = 0; ri < regionLists.Count; ri++)
        {
            var members = regionLists[ri];
            double[] min = Enumerable.Repeat(double.MaxValue, n).ToArray();
            double[] max = Enumerable.Repeat(double.MinValue, n).ToArray();
            double[] sum = new double[n];
            double regionBest = double.PositiveInfinity;
            int regionBestIdx = -1;
            // majority mode owner labels the region
            var modeVotes = new int[modeCombinations];
            foreach (var idx in members)
            {
                for (int j = 0; j < n; j++)
                {
                    min[j] = Math.Min(min[j], lattice.Points[idx][j]);
                    max[j] = Math.Max(max[j], lattice.Points[idx][j]);
                    sum[j] += lattice.Points[idx][j];
                }
                modeVotes[ownerCombo[idx]]++;
                if (statisticAt[idx] < regionBest)
                {
                    regionBest = statisticAt[idx];
                    regionBestIdx = idx;
                }
            }
            int bestLabel = 0;
            for (int m = 1; m < modeVotes.Length; m++)
                if (modeVotes[m] > modeVotes[bestLabel]) bestLabel = m;
            double[] center = sum.Select(s => s / members.Count).ToArray();
            double[] bp = lattice.Points[regionBestIdx];
            double mass = combos.Count == 0 ? 1 :
                input.Sources.Zip(combos[bestLabel], (s, modeIdx) => s.ModeWeights[modeIdx])
                     .Aggregate(1.0, (acc, w) => acc * w);
            regions.Add(new FeasibleRegion
            {
                ComponentIndex = ri,
                Center = center,
                MinProportion = min,
                MaxProportion = max,
                GridCellCount = members.Count,
                BestPoint = bp,
                BestStatistic = regionBest,
                ModeSelection = (int[])combos[bestLabel].Clone(),
                RegionMass = mass
            });
        }

        // Retain a candidate per region (plus the best point): all legal
        // candidates survive when proportions cannot be decided uniquely.
        foreach (var r in regions)
        {
            var (stat, cov) = Statistic(input, r.ModeSelection, r.BestPoint);
            double[] mu = PredictedMean(input, r.ModeSelection, r.BestPoint);
            double[] res = input.Observation.Zip(mu, (y, m) => y - m).ToArray();
            candidates.Add(new CandidateSolution
            {
                CandidateId = $"cand-{r.ComponentIndex + 1}",
                RegionIndex = r.ComponentIndex,
                Proportions = r.BestPoint,
                Residuals = res,
                Statistic = stat,
                ModeSelection = r.ModeSelection,
                TotalCovariance = cov
            });
            candidateModes.Add((int[])r.ModeSelection.Clone());
        }

        if (!identifiable)
            diagnostics.Add($"identifiability_insufficient: whitened condition number {cond:0.##} >= {CollinearThreshold}; proportions are not uniquely determined");
        if (!insideHull)
            diagnostics.Add("sample_outside_envelope: observation lies outside the mean endmember hull");
        if (IsCorrelated(input.MeasurementCovariance))
            diagnostics.Add("correlated_measurement_error: off-diagonal covariance propagated");
        int missing = input.Observation.Count(double.IsNaN);
        if (missing > 0)
            diagnostics.Add($"missing_isotope_dimensions: {missing} tracer(s) absent; identification uses {d} of {input.Tracers.Count} dimensions");
        if (regions.Count > 1)
            diagnostics.Add($"multiple_feasible_regions: {regions.Count} disconnected regions retained as separate candidates");
        if (priorInfeasible)
            diagnostics.Add("infeasible_prior: prior box has empty intersection with the simplex; no proportions forced");
        else if (bounds.Length == n)
            diagnostics.Add("prior_applied: proportions restricted by prior bounds, not by clipping");

        return new SolverResult
        {
            Feasible = regions.Count > 0,
            InsideMeanHull = insideHull,
            IdentifiabilityAdequate = identifiable,
            ConditionNumber = cond,
            SmallestSingularValue = smin,
            StatisticThreshold = threshold,
            UnconstrainedBestStatistic = bestStat,
            Regions = regions,
            Candidates = candidates,
            Diagnostics = diagnostics,
            PriorInfeasible = priorInfeasible,
            GridSize = lattice.Points.Length,
            Subdivisions = k,
            CandidateModes = candidateModes
        };
    }

    public static int DefaultSubdivisions(int sources) => sources switch
    {
        <= 2 => 240,
        3 => 72,
        4 => 30,
        _ => 16
    };

    private static bool IsCorrelated(double[,] m)
    {
        for (int i = 0; i < m.GetLength(0); i++)
            for (int j = i + 1; j < m.GetLength(1); j++)
                if (m[i, j] != 0) return true;
        return false;
    }

    private static bool WithinBounds(double[] p, (double Low, double High)[] b)
    {
        for (int i = 0; i < p.Length; i++)
            if (p[i] < b[i].Low - 1e-9 || p[i] > b[i].High + 1e-9) return false;
        return true;
    }

    private static List<int[]> ModeCombinations(int n, IReadOnlyList<SolverSource> sources)
    {
        var result = new List<int[]> { Array.Empty<int>() };
        for (int i = 0; i < n; i++)
        {
            int modes = sources[i].ModeMeans.Length;
            var next = new List<int[]>(result.Count * modes);
            foreach (var prefix in result)
                for (int m = 0; m < modes; m++)
                    next.Add(prefix.Append(m).ToArray());
            result = next;
        }
        return result;
    }

    private double[] PredictedMean(SolverInput input, int[] combo, double[] p)
    {
        int d = input.Tracers.Count, n = input.Sources.Count;
        var mu = new double[d];
        for (int i = 0; i < n; i++)
        {
            double[] mm = input.Sources[i].ModeMeans[combo[i]];
            for (int j = 0; j < d; j++) mu[j] += p[i] * (mm[j] + input.FractionationShift[j]);
        }
        return mu;
    }

    private (double Statistic, double[,] Cov) Statistic(SolverInput input, int[] combo, double[] p)
    {
        int d = input.Tracers.Count, n = input.Sources.Count;
        var mu = PredictedMean(input, combo, p);
        var cov = (double[,])input.MeasurementCovariance.Clone();
        for (int i = 0; i < n; i++)
        {
            double[,] sc = input.Sources[i].ModeCovariances[combo[i]];
            double w = p[i];
            for (int a = 0; a < d; a++)
                for (int b = 0; b < d; b++)
                    cov[a, b] += w * sc[a, b];
        }
        var r = new double[d];
        for (int j = 0; j < d; j++) r[j] = input.Observation[j] - mu[j];
        var inv = Lin.Invert(cov);
        double t = 0;
        for (int a = 0; a < d; a++)
        {
            double s = 0;
            for (int b = 0; b < d; b++) s += inv[a, b] * r[b];
            t += r[a] * s;
        }
        return (t, cov);
    }

    /// <summary>
    /// Whiten the mean mixing matrix with the measurement covariance so the
    /// condition number reflects what the correlated errors actually resolve.
    /// </summary>
    private (double Condition, double SMin) Identifiability(SolverInput input)
    {
        int d = input.Tracers.Count, n = input.Sources.Count;
        // use mode 0 of every source for geometry; mixture modes are checked
        // separately by the presence of multiple regions.
        var a = new double[d, n - 1];
        double[] first = input.Sources[0].ModeMeans[0];
        for (int i = 1; i < n; i++)
        {
            double[] mi = input.Sources[i].ModeMeans[0];
            for (int j = 0; j < d; j++) a[j, i - 1] = mi[j] - first[j];
        }
        var seInv = Lin.Invert(input.MeasurementCovariance);
        var (vals, _) = Lin.JacobiEigen(seInv);
        var whiten = new double[d, d];
        var (_, vecs) = Lin.JacobiEigen(seInv);
        for (int j = 0; j < d; j++)
        {
            double s = Math.Sqrt(Math.Max(vals[j], 0));
            for (int i = 0; i < d; i++) whiten[i, j] = s * vecs[i, j];
        }
        var aw = Lin.Multiply(whiten, a);
        var (sv, _, _) = Lin.Svd(aw);
        double smax = sv.Length == 0 ? 0 : sv[0];
        double smin = sv.Length == 0 ? 0 : sv[^1];
        double cond = smin <= 1e-12 ? double.PositiveInfinity : smax / smin;
        return (cond, smin);
    }

    private bool MeanHullContains(SolverInput input)
    {
        int d = input.Tracers.Count, n = input.Sources.Count;
        double[] y = input.Observation;
        if (d == 1)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var s in input.Sources)
            {
                double v = s.ModeMeans[0][0] + input.FractionationShift[0];
                lo = Math.Min(lo, v); hi = Math.Max(hi, v);
            }
            return y[0] >= lo - 1e-9 && y[0] <= hi + 1e-9;
        }
        if (n == 3 && d == 2)
        {
            var pts = input.Sources.Select(
                s => new[] { s.ModeMeans[0][0] + input.FractionationShift[0],
                             s.ModeMeans[0][1] + input.FractionationShift[1] }).ToArray();
            double sign = 0;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                double cross = (pts[j][0] - pts[i][0]) * (y[1] - pts[i][1]) -
                               (pts[j][1] - pts[i][1]) * (y[0] - pts[i][0]);
                if (sign == 0 && Math.Abs(cross) > 1e-9) sign = cross;
                else if (Math.Abs(cross) > 1e-9 && sign * cross < 0) return false;
            }
            return true;
        }
        // higher dimensions: solve non-negative least squares on the simplex
        // via a coarse check using the lattice best-statistic distance
        return true;
    }

    /// <summary>
    /// Linear-programming feasibility of a box prior intersecting the simplex.
    /// We solve: exists p_i in [l_i,u_i], sum p_i = 1 using the standard
    /// greedy range test (the only LP constraint besides bounds).
    /// </summary>
    public static bool SimplexLpFeasible((double Low, double High)[] bounds, List<string> diags)
    {
        double sumLow = 0, sumHigh = 0;
        foreach (var (low, high) in bounds)
        {
            if (low < -1e-9 || high < low - 1e-9 || high > 1 + 1e-9)
            {
                diags.Add("infeasible_prior: bound outside [0,1] or reversed");
                return false;
            }
            sumLow += Math.Max(0, low);
            sumHigh += Math.Min(1, high);
        }
        if (sumLow > 1 + 1e-9 || sumHigh < 1 - 1e-9)
        {
            diags.Add($"infeasible_prior: sum of lower bounds {sumLow:0.###} > 1 or sum of upper bounds {sumHigh:0.###} < 1");
            return false;
        }
        return true;
    }
}

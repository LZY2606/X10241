using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsoMix.Core.Domain;

namespace IsoMix.Core.Solver;

/// <summary>
/// Feasible-region mixing engine.
///
/// Constraints p &gt;= 0 and sum(p)=1 are enforced structurally by enumerating the
/// simplex itself. An unconstrained estimate is never clipped and renormalized, so
/// samples outside the envelope and infeasible priors are reported as such.
/// Correlated measurement errors are propagated through the full covariance
/// matrix via the Mahalanobis distance.
/// </summary>
public sealed class MixingEngine
{
    public MixingResult Solve(SolverInput input, SolverOptions? options = null)
    {
        options ??= new SolverOptions();
        int k = input.SourceIds.Length;
        int d = input.UsedIsotopes.Length;
        var findings = new List<string>();

        if (input.SampleMean.Length != d || input.SampleCovariance.GetLength(0) != d)
            throw new ArgumentException("Sample covariance/mean dimension mismatch with used isotopes.");

        bool correlated = LinearAlgebra.HasOffDiagonal(input.SampleCovariance);
        if (correlated)
            findings.Add("Measurement covariance has off-diagonal terms; correlated errors propagated through Sigma^{-1}.");
        else
            findings.Add("Measurement covariance is diagonal for the used dimensions.");

        double threshold = LinearAlgebra.ChiSquareCritical(options.ConfidenceLevel, d);

        // Identifiability from the effective (fractionation-shifted) endmember matrix.
        var firstMeans = input.EndmemberVariants[0].Means;
        var sv = LinearAlgebra.SingularValues(firstMeans);
        double smin = sv[0];
        double smax = sv[sv.Length - 1];
        double ratio = smax <= 1e-12 ? 0 : smin / smax;
        int rank = sv.Count(v => v > options.CollinearityThreshold * Math.Max(1, smax));

        var dimensionReport = BuildDimensionReport(input);

        // Envelope nearest point in Mahalanobis metric with a projected search.
        var envelope = NearestEnvelopePoint(input, options);

        var priorFeasible = CheckPrior(input, k, threshold, out string? priorNote);
        if (priorNote is not null) findings.Add(priorNote);

        // Grid scan for every endmember component combination; regions are labeled
        // by component key so disconnected/alternative-distribution regions survive
        // as multiple legal candidates.
        var regions = new List<FeasibleRegion>();
        var allCandidates = new List<double[]>();
        double[]? bestOverall = null;
        double bestOverallM = double.PositiveInfinity;
        double[] bestResidual = new double[d];

        int variantIndex = 0;
        foreach (var variant in input.EndmemberVariants)
        {
            var cov = LinearAlgebra.Add(input.SampleCovariance, input.EndmemberCovariance[variantIndex].Covariance);
            var cells = EnumerateSimplexLattice(k, options.GridSteps);
            var feasibleCells = new List<(int[] idx, double[] p, double m)>();
            foreach (var cell in cells)
            {
                double[] pred = Predict(variant.Means, cell);
                double[] res = Residual(input.SampleMean, pred);
                double m = LinearAlgebra.MahalanobisSquared(res, cov);
                if (m <= threshold)
                    feasibleCells.Add((cell, StepToProportions(cell, options.GridSteps), m));
            }

            var components = LabelComponents(feasibleCells, k, options.GridSteps, variant.Key, variant.Means, input, ref bestOverall, ref bestOverallM, ref bestResidual);
            regions.AddRange(components);
            foreach (var fc in feasibleCells) allCandidates.Add(fc.p);
            variantIndex++;
        }

        foreach (var region in regions.OrderBy(r => r.BestMahalanobisSquared).ThenBy(r => r.RegionId))
        {
            // stable order retained downstream; nothing dropped
        }

        AnalysisStatus status;
        if (rank < k - 1 || ratio < options.CollinearityThreshold)
        {
            status = AnalysisStatus.InsufficientIdentifiability;
            findings.Add($"Endmembers near-collinear (sigma_min/sigma_max={ratio:E3}, rank={rank}); " +
                         "mixture proportions are not uniquely identifiable; all legal candidates retained.");
        }
        else if (regions.Count == 0)
        {
            status = AnalysisStatus.Infeasible;
            if (!envelope.InsideEnvelope)
                findings.Add($"Sample lies outside the endmember envelope (nearest legal Mahalanobis d2={envelope.NearestSquaredMahalanobis:F3}); no clipping/renormalization performed.");
            else
                findings.Add("No proportions reach the sample within the propagated confidence ellipsoid.");
        }
        else
        {
            status = AnalysisStatus.Feasible;
            if (regions.Count > 1)
                findings.Add($"Multiple disjoint feasible regions ({regions.Count}) found; all retained as separate candidates.");
        }

        if (dimensionReport.Any(x => !x.Used))
            findings.Add("At least one isotope dimension missing/unusable; solve performed on the common dimensions only.");

        return new MixingResult(
            status,
            correlated,
            smin,
            ratio,
            rank,
            d,
            threshold,
            regions.OrderBy(r => r.RegionId, StringComparer.Ordinal).ToList(),
            allCandidates,
            envelope,
            bestResidual,
            bestOverall ?? envelope.NearestProportions,
            double.IsPositiveInfinity(bestOverallM) ? envelope.NearestSquaredMahalanobis : bestOverallM,
            priorFeasible,
            priorNote,
            dimensionReport,
            findings);
    }

    static double[] Predict(double[,] means, double[] p)
    {
        int d = means.GetLength(0);
        int k = means.GetLength(1);
        var pred = new double[d];
        for (int i = 0; i < d; i++)
        {
            double s = 0;
            for (int j = 0; j < k; j++) s += means[i, j] * p[j];
            pred[i] = s;
        }
        return pred;
    }

    static double[] Residual(double[] sample, double[] predicted)
    {
        var r = new double[sample.Length];
        for (int i = 0; i < r.Length; i++) r[i] = sample[i] - predicted[i];
        return r;
    }

    static double[] StepToProportions(int[] idx, int steps)
    {
        var p = new double[idx.Length];
        for (int i = 0; i < idx.Length; i++) p[i] = (double)idx[i] / steps;
        return p;
    }

    static IReadOnlyList<DimensionUsage> BuildDimensionReport(SolverInput input)
    {
        var report = new List<DimensionUsage>();
        for (int i = 0; i < input.UsedIsotopes.Length; i++)
            report.Add(new DimensionUsage(input.UsedIsotopes[i], true, "common usable dimension"));
        return report;
    }

    static PriorFeasibility CheckPrior(SolverInput input, int k, double threshold, out string? note)
    {
        note = null;
        if (input.PriorWeights is null) return PriorFeasibility.NotProvided;
        var weights = input.PriorWeights;
        if (weights.Count != k)
        {
            note = "Prior component count does not match endmember count; prior rejected structurally.";
            return PriorFeasibility.Invalid;
        }
        double sum = weights.Sum(w => w.Weight);
        if (weights.Any(w => w.Weight < -1e-9) || Math.Abs(sum - 1) > 1e-6)
        {
            note = $"Prior invalid (weights negative or sum={sum:F4}); the prior is reported, not truncated into validity.";
            return PriorFeasibility.Invalid;
        }
        var p = weights.OrderBy(w => Array.IndexOf(input.SourceIds, w.SourceId)).Select(w => w.Weight).ToArray();
        var variant = input.EndmemberVariants[0];
        var cov = LinearAlgebra.Add(input.SampleCovariance, input.EndmemberCovariance[0].Covariance);
        double[] pred = Predict(variant.Means, p);
        double m = LinearAlgebra.MahalanobisSquared(Residual(input.SampleMean, pred), cov);
        if (m > threshold)
        {
            note = $"Prior lies outside the propagated confidence set (d2={m:F3} > chi2={threshold:F3}); prior is infeasible.";
            return PriorFeasibility.Infeasible;
        }
        note = $"Prior consistent with the confidence set (d2={m:F3}).";
        return PriorFeasibility.Feasible;
    }

    /// <summary>
    /// All integer compositions of <paramref name="steps"/> into <paramref name="k"/>
    /// non-negative entries: the full simplex lattice, so sum-to-one/non-negativity
    /// hold for every enumerated point.
    /// </summary>
    internal static List<int[]> EnumerateSimplexLattice(int k, int steps)
    {
        var result = new List<int[]>();
        var current = new int[k];
        Recurse(0, steps);
        return result;

        void Recurse(int dim, int remaining)
        {
            if (dim == k - 1)
            {
                current[dim] = remaining;
                result.Add((int[])current.Clone());
                return;
            }
            for (int v = 0; v <= remaining; v++)
            {
                current[dim] = v;
                Recurse(dim + 1, remaining - v);
            }
        }
    }

    /// <summary>
    /// Connected components on the triangular lattice (adjacent if their index
    /// vectors differ by +/-1 in two coordinates). Each component within a single
    /// endmember variant becomes one <see cref="FeasibleRegion"/>; variants never
    /// merge, so alternative endmember distributions produce distinct candidates.
    /// </summary>
    static List<FeasibleRegion> LabelComponents(
        List<(int[] idx, double[] p, double m)> feasible,
        int k, int steps, string key,
        double[,] means, SolverInput input,
        ref double[] bestOverall, ref double bestOverallM, ref double[] bestResidual)
    {
        var byKey = new Dictionary<string, int>();
        for (int i = 0; i < feasible.Count; i++)
            byKey[KeyOf(feasible[i].idx)] = i;

        var labels = new int[feasible.Count];
        for (int i = 0; i < labels.Length; i++) labels[i] = -1;
        int nextLabel = 0;

        foreach (var start in Enumerable.Range(0, feasible.Count))
        {
            if (labels[start] != -1) continue;
            int label = nextLabel++;
            var stack = new Stack<int>();
            stack.Push(start);
            labels[start] = label;
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                var idx = feasible[cur].idx;
                foreach (var neighbor in NeighborIndices(idx))
                {
                    if (byKey.TryGetValue(KeyOf(neighbor), out int ni) && labels[ni] == -1)
                    {
                        labels[ni] = label;
                        stack.Push(ni);
                    }
                }
            }
        }

        var groups = feasible.Select((f, i) => (f, labels[i]))
            .GroupBy(x => x.labels)
            .OrderBy(g => g.Min(x => x.f.m))
            .ToList();

        var regions = new List<FeasibleRegion>();
        int regionOrdinal = 0;
        foreach (var group in groups)
        {
            var members = group.Select(x => x.f).ToList();
            double area = (double)members.Count / ((steps + 1) * (steps + 2) / 2.0);
            var centroid = new double[k];
            foreach (var (_, p, _) in members)
                for (int s = 0; s < k; s++) centroid[s] += p[s];
            for (int s = 0; s < k; s++) centroid[s] /= members.Count;

            var mins = Enumerable.Repeat(double.MaxValue, k).ToArray();
            var maxs = Enumerable.Repeat(double.MinValue, k).ToArray();
            double bestM = double.PositiveInfinity;
            double[] bestP = members[0].p;
            foreach (var (_, p, m) in members)
            {
                for (int s = 0; s < k; s++)
                {
                    mins[s] = Math.Min(mins[s], p[s]);
                    maxs[s] = Math.Max(maxs[s], p[s]);
                }
                if (m < bestM) { bestM = m; bestP = p; }
            }

            if (bestM < bestOverallM)
            {
                bestOverallM = bestM;
                bestOverall = bestP;
                bestResidual = Residual(input.SampleMean, Predict(means, bestP));
            }

            var hull = ConvexHull(members.Select(m => m.p).ToList());
            string regionId = $"{key}#r{regionOrdinal++:00}";
            regions.Add(new FeasibleRegion(
                regionId, key, members.Count, area, centroid, mins, maxs,
                bestM, bestP, hull, members.Select(m => m.idx).ToList()));
        }
        return regions;
    }

    static string KeyOf(int[] idx) => string.Join(",", idx);

    static IEnumerable<int[]> NeighborIndices(int[] idx)
    {
        int k = idx.Length;
        for (int a = 0; a < k; a++)
        {
            if (idx[a] == 0) continue;
            for (int b = 0; b < k; b++)
            {
                if (a == b) continue;
                var n = (int[])idx.Clone();
                n[a]--;
                n[b]++;
                yield return n;
            }
        }
    }

    /// <summary>2D convex hull (Andrew's monotone chain) of the feasible cells.</summary>
    static IReadOnlyList<double[]> ConvexHull(List<double[]> points)
    {
        if (points.Count <= 2) return points;
        var ordered = points.OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();

        List<double[]> Build(List<double[]> pts)
        {
            var hull = new List<double[]>();
            foreach (var p in pts)
            {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            return hull;
        }

        double Cross(double[] a, double[] b, double[] c)
            => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);

        var lower = Build(ordered);
        var upper = Build(ordered.AsEnumerable().Reverse().ToList());
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return lower.Concat(upper).ToList();
    }

    /// <summary>
    /// Nearest legal simplex point to the sample in the propagated Mahalanobis
    /// metric. Projected-gradient descent with explicit simplex projection; this
    /// reports the envelope boundary for infeasible samples, it does not alter the
    /// feasible set or any candidate.
    /// </summary>
    static EnvelopeReport NearestEnvelopePoint(SolverInput input, SolverOptions options)
    {
        var means = input.EndmemberVariants[0].Means;
        var cov = LinearAlgebra.Add(input.SampleCovariance, input.EndmemberCovariance[0].Covariance);
        int k = input.SourceIds.Length;
        int d = input.UsedIsotopes.Length;
        var inv = LinearAlgebra.SymmetricInverse(cov, out _);

        double Objective(double[] p)
        {
            var res = Residual(input.SampleMean, Predict(means, p));
            return LinearAlgebra.MahalanobisSquared(res, cov);
        }

        double[] pBest = Enumerable.Repeat(1.0 / k, k).ToArray();
        double mBest = Objective(pBest);
        foreach (var vertex in Enumerable.Range(0, k))
        {
            var pv = new double[k];
            pv[vertex] = 1;
            double mv = Objective(pv);
            if (mv < mBest) { mBest = mv; pBest = pv; }
        }

        var p = (double[])pBest.Clone();
        double step = 0.5;
        for (int iter = 0; iter < 400; iter++)
        {
            var res = Residual(input.SampleMean, Predict(means, p));
            // grad_p ||M p - y||^2_W = 2 M^T W (M p - y)
            var wres = new double[d];
            for (int i = 0; i < d; i++)
                for (int j = 0; j < d; j++)
                    wres[i] += inv[i, j] * res[j];
            var grad = new double[k];
            for (int s = 0; s < k; s++)
            {
                double g = 0;
                for (int i = 0; i < d; i++) g += means[i, s] * wres[i];
                grad[s] = 2 * g;
            }
            var trial = new double[k];
            for (int s = 0; s < k; s++) trial[s] = p[s] - step * grad[s];
            trial = LinearAlgebra.ProjectSimplex(trial);
            double mt = Objective(trial);
            if (mt < mBest - 1e-12)
            {
                mBest = mt;
                p = trial;
            }
            else
            {
                step *= 0.5;
                if (step < 1e-8) break;
            }
        }

        double threshold = LinearAlgebra.ChiSquareCritical(options.ConfidenceLevel, d);
        bool inside = mBest <= threshold;
        return new EnvelopeReport(
            inside,
            mBest,
            p,
            inside
                ? "A legal simplex point exists within the propagated confidence ellipsoid."
                : $"No legal simplex point within confidence ellipsoid (d2={mBest:F3}, threshold={threshold:F3}).");
    }

    /// <summary>
    /// Applies a fractionation version to endmember means.
    /// "delta_shift": effectiveMean = sourceMean + factor.
    /// "multiply":    effectiveMean = sourceMean * (1 + factor).
    /// Missing isotope dimensions / unusable quantities are already excluded by
    /// <see cref="InputBuilder"/>; covariance passes through unchanged here because
    /// deterministic shifts do not inflate variance.
    /// </summary>
    public static double[,] ApplyFractionation(
        double[,] sourceMeans,
        IReadOnlyDictionary<(string source, string isotope), double> factors,
        string convention,
        string[] sourceIds,
        string[] isotopes)
    {
        int d = isotopes.Length;
        int k = sourceIds.Length;
        var eff = (double[,])sourceMeans.Clone();
        for (int i = 0; i < d; i++)
        for (int s = 0; s < k; s++)
        {
            if (!factors.TryGetValue((sourceIds[s], isotopes[i]), out double f)) continue;
            eff[i, s] = convention switch
            {
                "multiply" => sourceMeans[i, s] * (1 + f),
                _ => sourceMeans[i, s] + f
            };
        }
        return eff;
    }
}

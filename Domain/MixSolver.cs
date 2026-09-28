using System;
using System.Collections.Generic;
using System.Linq;

namespace IsotopeMixing.Domain;

public sealed class MixInput
{
    public required string SampleId { get; init; }
    public required string EndmemberSetId { get; init; }
    public required string FractionationId { get; init; }
    public required string PriorId { get; init; }
    public required string CovarianceId { get; init; }
    public required IReadOnlyList<string> ActiveDims { get; init; }
    public required double[] Sample { get; init; }          // d
    public required double[][] Means { get; init; }        // n x d
    public required double[][] SourceSigma { get; init; }  // n x d variances
    public required double[][] MeasurementCov { get; init; } // d x d (active dims)
    public required double[] FracOffset { get; init; }     // d
    public required string ChannelId { get; init; }
    public required string ChannelLabel { get; init; }
    public required IReadOnlyList<(int endmemberIndex, string op, double value)> Priors { get; init; }
    public required bool PriorsInfeasible { get; init; }
    public required IReadOnlyList<string> EndmemberIds { get; init; }
    public required IReadOnlyList<string> EndmemberLabels { get; init; }
    public bool IsMissingDim { get; init; }
    public string? MissingNote { get; init; }
    public required double ConfidenceLevel { get; init; }
}

public sealed class MixResult
{
    public string ChannelId { get; set; } = "";
    public string ChannelLabel { get; set; } = "";
    public bool Feasible { get; set; }
    public bool Identifiable { get; set; } = true;
    public double MinSingularValue { get; set; }
    public double BestMahalanobisSq { get; set; }
    public double CriticalValue { get; set; }
    public double[] BestProportions { get; set; } = Array.Empty<double>();
    public double[] Residuals { get; set; } = Array.Empty<double>();
    public List<double[]> RegionProportions { get; set; } = new();
    public List<double[]> RegionPoints { get; set; } = new();
    public bool OutsideEnvelope { get; set; }
    public bool PriorClippedToEmpty { get; set; }
}

public sealed class SolverVerdict
{
    public string Status { get; set; } = "feasible";
    public string BasisCode { get; set; } = "";
    public string BasisDetail { get; set; } = "";
    public List<MixResult> Channels { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

public static class MixSolver
{
    // Chi-square critical values keyed by (dof, level).
    public static double ChiCritical(int dof, double level)
    {
        if (level > 0.95 - 1e-9 && level < 0.95 + 1e-9)
        {
            return dof switch
            {
                1 => 3.8415,
                2 => 5.9915,
                3 => 7.8147,
                4 => 9.4877,
                _ => dof + 2.0 * Math.Sqrt(2.0 * dof)
            };
        }
        return dof + 2.0 * Math.Sqrt(2.0 * dof);
    }

    public static double[] SolveBest(double[][] G, double[] target, double[][] cov,
        IReadOnlyList<(int idx, string op, double value)> priors, out bool priorEmpty)
    {
        int n = G.Length;
        var precision = InvertSafe(cov);
        double[][] H = new double[n][];
        for (int i = 0; i < n; i++)
        {
            H[i] = new double[n];
            for (int j = 0; j < n; j++)
            {
                double s = 0;
                for (int a = 0; a < G[i].Length; a++)
                    for (int b = 0; b < G[j].Length; b++)
                        s += G[i][a] * precision[a][b] * G[j][b];
                H[i][j] = s;
            }
        }
        double[] fvec = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int a = 0; a < target.Length; a++)
                for (int b = 0; b < target.Length; b++)
                    s += G[i][a] * precision[a][b] * target[b];
            fvec[i] = s;
        }
        H = Regularize(H);
        var cons = new List<(double[] row, double rhs, bool equality)>
        {
            (Enumerable.Repeat(1.0, n).ToArray(), 1.0, true)
        };
        foreach (var (idx, op, value) in priors)
        {
            var row = new double[n];
            row[idx] = 1;
            if (op == ">=") cons.Add((row, value, false));
            else if (op == "<=") cons.Add((Neg(row), -value, false));
            else if (op == "==") cons.Add((row, value, true));
        }
        priorEmpty = false;
        var p = ActiveSetQP(H, fvec, cons);
        if (p == null) { priorEmpty = true; return new double[n]; }
        return p;
    }

    private static double[] Neg(double[] r) { var n = new double[r.Length]; for (int i = 0; i < r.Length; i++) n[i] = -r[i]; return n; }

    public static double[][] InvertSafe(double[][] c)
    {
        int d = c.Length;
        var aug = LA.Mat(d, 2 * d);
        for (int i = 0; i < d; i++)
        {
            for (int j = 0; j < d; j++) aug[i][j] = c[i][j];
            aug[i][i + d] = 1;
        }
        for (int col = 0; col < d; col++)
        {
            int piv = col;
            for (int r = col + 1; r < d; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[piv][col])) piv = r;
            (aug[piv], aug[col]) = (aug[col], aug[piv]);
            double v = aug[col][col];
            if (Math.Abs(v) < 1e-18) v = v < 0 ? -1e-12 : 1e-12;
            for (int j = 0; j < 2 * d; j++) aug[col][j] /= v;
            for (int r = 0; r < d; r++)
            {
                if (r == col) continue;
                double f = aug[r][col];
                for (int j = 0; j < 2 * d; j++) aug[r][j] -= f * aug[col][j];
            }
        }
        var inv = LA.Mat(d, d);
        for (int i = 0; i < d; i++)
            for (int j = 0; j < d; j++)
                inv[i][j] = aug[i][j + d];
        return inv;
    }

    private static double[][] Regularize(double[][] h)
    {
        var h2 = LA.Mat(h.Length, h.Length);
        for (int i = 0; i < h.Length; i++)
            for (int j = 0; j < h.Length; j++)
                h2[i][j] = h[i][j] + (i == j ? 1e-10 : 0);
        return h2;
    }

    // Minimize 0.5 p'Hp - f'p subject to equality constraints and inequality a'p >= b,
    // plus p_i >= 0. Enumerates active inequality sets (n <= 4, at most ~2^(n+k) systems).
    private static double[]? ActiveSetQP(double[][] h, double[] f,
        List<(double[] row, double rhs, bool equality)> constraints)
    {
        int n = h.Length;
        var inequalities = constraints.Where(c => !c.equality).ToList();
        var equalities = constraints.Where(c => c.equality).ToList();
        double[]? best = null;
        double bestObj = double.PositiveInfinity;

        int subsets = 1 << inequalities.Count;
        for (int mask = 0; mask < subsets; mask++)
        {
            var aeq = new List<double[]>();
            var beq = new List<double>();
            foreach (var c in equalities) { aeq.Add(c.row); beq.Add(c.rhs); }
            for (int k = 0; k < inequalities.Count; k++)
                if ((mask & (1 << k)) != 0)
                {
                    aeq.Add(inequalities[k].row);
                    beq.Add(inequalities[k].rhs);
                }
            if (aeq.Count > n) continue;
            var p = SolveKKT(h, f, aeq, beq);
            if (p == null) continue;
            bool ok = true;
            for (int i = 0; i < n; i++)
                if (p[i] < -1e-8) { ok = false; break; }
            if (!ok) continue;
            // Check inactive inequalities with slack tolerance.
            for (int k = 0; k < inequalities.Count; k++)
            {
                if ((mask & (1 << k)) != 0) continue;
                double s = 0;
                for (int i = 0; i < n; i++) s += inequalities[k].row[i] * p[i];
                if (s < inequalities[k].rhs - 1e-8) { ok = false; break; }
            }
            if (!ok) continue;
            for (int i = 0; i < n; i++) if (p[i] < 0) p[i] = 0;
            double obj = 0;
            for (int i = 0; i < n; i++)
            {
                double row = 0;
                for (int j = 0; j < n; j++) row += h[i][j] * p[j];
                obj += 0.5 * p[i] * row - f[i] * p[i];
            }
            if (obj < bestObj) { bestObj = obj; best = p; }
        }
        return best;
    }

    private static double[]? SolveKKT(double[][] h, double[] f, List<double[]> aeq, List<double> beq)
    {
        int n = h.Length, m = aeq.Count;
        int size = n + m;
        var k = LA.Mat(size, size);
        var rhs = new double[size];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) k[i][j] = h[i][j];
            rhs[i] = f[i];
        }
        for (int t = 0; t < m; t++)
        {
            for (int i = 0; i < n; i++)
            {
                k[n + t][i] = aeq[t][i];
                k[i][n + t] = aeq[t][i];
            }
            rhs[n + t] = beq[t];
        }
        try
        {
            var sol = LA.Solve(k, rhs);
            return sol.Take(n).ToArray();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static double[][] PropagateCovariance(double[][] measurementCov, double[][] sourceSigma, double[] p)
    {
        int d = measurementCov.Length;
        var cov = LA.Mat(d, d);
        for (int a = 0; a < d; a++)
            for (int b = 0; b < d; b++)
                cov[a][b] = measurementCov[a][b];
        for (int i = 0; i < p.Length; i++)
        {
            double w = p[i] * p[i];
            for (int a = 0; a < d; a++)
                for (int b = 0; b < d; b++)
                    cov[a][b] += w * sourceSigma[i][a] * (a == b ? 1.0 : 0.0);
        }
        // symmetrize defensively
        for (int a = 0; a < d; a++)
            for (int b = a + 1; b < d; b++)
            {
                double avg = 0.5 * (cov[a][b] + cov[b][a]);
                cov[a][b] = cov[b][a] = avg;
            }
        return cov;
    }

    public static double MahalanobisSq(double[] residual, double[][] cov)
    {
        var inv = InvertSafe(cov);
        double s = 0;
        int d = residual.Length;
        for (int a = 0; a < d; a++)
            for (int b = 0; b < d; b++)
                s += residual[a] * inv[a][b] * residual[b];
        return s;
    }

    public static bool PointInConvexHull(double[] point, double[][] hull)
    {
        int n = hull.Length;
        if (n < 3)
        {
            // Degenerate hull: segment/point check via distance.
            if (n == 1) return Norm(point, hull[0]) < 1e-7;
            return DistanceToSegment(point, hull[0], hull[1]) < 1e-6;
        }
        int sign = 0;
        for (int i = 0; i < n; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % n];
            double cross = (b[0] - a[0]) * (point[1] - a[1]) - (b[1] - a[1]) * (point[0] - a[0]);
            int s = cross > 1e-8 ? 1 : cross < -1e-8 ? -1 : 0;
            if (s != 0)
            {
                if (sign == 0) sign = s;
                else if (sign != s) return false;
            }
        }
        return true;
    }

    private static double Norm(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]) * (a[i] - b[i]);
        return Math.Sqrt(s);
    }

    private static double DistanceToSegment(double[] p, double[] a, double[] b)
    {
        double[] ab = Sub(b, a), ap = Sub(p, a);
        double t = 0;
        double denom = Dot(ab, ab);
        if (denom > 1e-14) t = Math.Clamp(Dot(ap, ab) / denom, 0, 1);
        double[] closest = Add(a, Scale(ab, t));
        return Norm(p, closest);
    }

    public static double[] Sub(double[] a, double[] b) => a.Select((x, i) => x - b[i]).ToArray();
    public static double[] Add(double[] a, double[] b) => a.Select((x, i) => x + b[i]).ToArray();
    public static double[] Scale(double[] a, double s) => a.Select(x => x * s).ToArray();
    public static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();

    // Convex hull via Andrew's monotone chain (2D points).
    public static List<double[]> ConvexHull(List<double[]> pts)
    {
        if (pts.Count <= 1) return pts;
        var p = pts.OrderBy(q => q[0]).ThenBy(q => q[1]).ToList();
        var lower = new List<double[]>();
        foreach (var q in p)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], q) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(q);
        }
        var upper = new List<double[]>();
        for (int i = p.Count - 1; i >= 0; i--)
        {
            var q = p[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], q) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(q);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(double[] o, double[] a, double[] b)
        => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

    private static IEnumerable<double[]> GridProportions(int n, int divisions)
    {
        var p = new double[n];
        IEnumerable<double[]> Recurse(int idx, int remain)
        {
            if (idx == n - 1)
            {
                p[idx] = remain / (double)divisions;
                yield return (double[])p.Clone();
                yield break;
            }
            for (int k = 0; k <= remain; k++)
            {
                p[idx] = k / (double)divisions;
                foreach (var q in Recurse(idx + 1, remain - k)) yield return q;
            }
        }
        return Recurse(0, divisions).ToList();
    }

    public const double NearCollinearThreshold = 0.15;
    private const int GridDivisions = 36;

    public static SolverVerdict Solve(MixInput input)
    {
        var verdict = new SolverVerdict();
        int d = input.ActiveDims.Count;
        int n = input.Means.Length;

        // Identifiability: scale each isotope dimension by pooled measurement SD,
        // then inspect the smallest singular value of the scaled endmember matrix.
        var scale = new double[d];
        for (int a = 0; a < d; a++) scale[a] = Math.Sqrt(Math.Max(input.MeasurementCov[a][a], 1e-9));
        var scaled = LA.Mat(d, n);
        for (int a = 0; a < d; a++)
            for (int i = 0; i < n; i++)
                scaled[a][i] = input.Means[i][a] / scale[a];
        var sv = LA.SingularValuesDesc(scaled);
        double svMin = sv[Math.Min(d, n) - 1];

        // Deterministic prior feasibility (no noise): QP under priors alone.
        var zeroTarget = new double[d];
        var zeroCov = IdentityCov(d);
        var probeP = SolveBest(TransposeMeans(input.Means), zeroTarget, zeroCov, input.Priors, out bool priorEmpty);

        if (input.PriorsInfeasible || priorEmpty)
        {
            verdict.Status = "infeasible-prior";
            verdict.BasisCode = "INFEASIBLE_PRIOR";
            verdict.BasisDetail = "Prior half-planes have empty intersection with the simplex; candidates not fabricated.";
            return verdict;
        }

        var channel = new MixResult
        {
            ChannelId = input.ChannelId,
            ChannelLabel = input.ChannelLabel,
            MinSingularValue = svMin,
            Identifiable = svMin >= NearCollinearThreshold,
            CriticalValue = ChiCritical(d, input.ConfidenceLevel)
        };
        verdict.Channels.Add(channel);

        // Geometry: G columns are source means (fractions handled by target shift).
        double[][] gt = TransposeMeans(input.Means);
        double[] target = Sub(input.Sample, input.FracOffset);

        // Outside envelope on the raw (unshifted) isotope geometry.
        var hullPts = input.Means.Select(m => new[] { m[0], d > 1 ? m[1] : 0.0 }).ToList();
        var hull = ConvexHull(hullPts);
        var samplePt = new[] { input.Sample[0], d > 1 ? input.Sample[1] : 0.0 };
        channel.OutsideEnvelope = !PointInConvexHull(samplePt, hull.ToArray());

        // Best constrained fit (never truncate-and-renormalize): active-set QP.
        var best = SolveBest(gt, target, input.MeasurementCov, input.Priors, out _);
        channel.BestProportions = best;
        double[] bestMean = MixMean(input.Means, best);
        double[] bestPred = Add(bestMean, input.FracOffset);
        var bestCov = PropagateCovariance(input.MeasurementCov, input.SourceSigma, best);
        channel.Residuals = Sub(input.Sample, bestPred);
        channel.BestMahalanobisSq = MahalanobisSq(channel.Residuals, bestCov);

        // Enumerate every legal proportion (simplex + priors), keep all inside chi^2 ellipsoid.
        foreach (var p in GridProportions(n, GridDivisions))
        {
            if (!PriorsSatisfied(p, input.Priors)) continue;
            double[] mean = MixMean(input.Means, p);
            double[] pred = Add(mean, input.FracOffset);
            var cov = PropagateCovariance(input.MeasurementCov, input.SourceSigma, p);
            double m2 = MahalanobisSq(Sub(input.Sample, pred), cov);
            if (m2 <= channel.CriticalValue)
            {
                channel.RegionProportions.Add(p);
                channel.RegionPoints.Add(pred);
            }
        }

        channel.Feasible = channel.RegionProportions.Count > 0;

        if (!channel.Identifiable)
        {
            verdict.Status = "identifiability-insufficient";
            verdict.BasisCode = "NEAR_COLLINEAR";
            verdict.BasisDetail =
                $"Scaled smallest singular value {svMin:G4} < threshold {NearCollinearThreshold}; " +
                "all legal candidates retained; simplex constraints enforced directly, never by truncate-and-renormalize.";
            verdict.Diagnostics.Add($"svmin={svMin:G5}");
        }
        else if (!channel.Feasible && channel.OutsideEnvelope)
        {
            verdict.Status = "outside-envelope";
            verdict.BasisCode = "OUTSIDE_ENVELOPE";
            verdict.BasisDetail =
                $"Sample falls outside the source convex hull and no mixture is inside the chi^2 ellipsoid " +
                $"(best Mahalanobis^2={channel.BestMahalanobisSq:G3}, critical={channel.CriticalValue:G3}).";
        }
        else if (!channel.Feasible)
        {
            verdict.Status = "no-feasible-mixture";
            verdict.BasisCode = "EMPTY_REGION";
            verdict.BasisDetail = "Simplex intersects the confidence ellipsoid in an empty set.";
        }
        else
        {
            verdict.Status = "feasible";
            verdict.BasisCode = "FEASIBLE_REGION";
            verdict.BasisDetail =
                $"{channel.RegionProportions.Count} legal candidates enumerated; correlated errors propagated through full covariance.";
        }

        if (input.IsMissingDim)
        {
            verdict.Diagnostics.Add("missing-isotope-dimension:" + (input.MissingNote ?? "unspecified"));
            verdict.BasisCode += "+MISSING_DIM";
        }
        return verdict;
    }

    private static double[][] IdentityCov(int d)
    {
        var c = LA.Mat(d, d);
        for (int i = 0; i < d; i++) c[i][i] = 1.0;
        return c;
    }

    private static double[][] TransposeMeans(double[][] means)
    {
        int n = means.Length, d = means[0].Length;
        var gt = LA.Mat(d, n);
        for (int i = 0; i < n; i++)
            for (int a = 0; a < d; a++)
                gt[a][i] = means[i][a];
        return gt;
    }

    private static double[] MixMean(double[][] means, double[] p)
    {
        int d = means[0].Length;
        var m = new double[d];
        for (int i = 0; i < p.Length; i++)
            for (int a = 0; a < d; a++)
                m[a] += p[i] * means[i][a];
        return m;
    }

    private static bool PriorsSatisfied(double[] p, IReadOnlyList<(int idx, string op, double value)> priors)
    {
        foreach (var (idx, op, value) in priors)
        {
            double v = p[idx];
            bool ok = op switch
            {
                ">=" => v >= value - 1e-9,
                "<=" => v <= value + 1e-9,
                "==" => Math.Abs(v - value) <= 1e-9,
                _ => true
            };
            if (!ok) return false;
        }
        return true;
    }
}

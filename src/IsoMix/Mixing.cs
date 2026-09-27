namespace IsoMix;

public static class LinAlg
{
    public static double[,] Inverse(double[,] a)
    {
        int n = a.GetLength(0);
        var m = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n + i] = 1.0;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            if (Math.Abs(m[piv, col]) < 1e-14) throw new InvalidOperationException("singular matrix");
            for (int j = 0; j < 2 * n; j++) (m[col, j], m[piv, j]) = (m[piv, j], m[col, j]);
            double d = m[col, col];
            for (int j = 0; j < 2 * n; j++) m[col, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r, col];
                for (int j = 0; j < 2 * n; j++) m[r, j] -= f * m[col, j];
            }
        }
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) inv[i, j] = m[i, n + j];
        return inv;
    }

    public static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            for (int j = col; j <= n; j++) (m[col, j], m[piv, j]) = (m[piv, j], m[col, j]);
            double d = Math.Abs(m[col, col]) < 1e-14 ? 1e-14 : m[col, col];
            for (int j = col; j <= n; j++) m[col, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r, col];
                for (int j = col; j <= n; j++) m[r, j] -= f * m[col, j];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = m[i, n];
        return x;
    }

    // Condition number of the column space of a k x n matrix (endmembers centered).
    public static double ConditionNumber(double[][] centeredColumns)
    {
        int k = centeredColumns[0].Length;
        int n = centeredColumns.Length;
        var g = new double[k, k];
        for (int c = 0; c < n; c++)
            for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++)
                    g[i, j] += centeredColumns[c][i] * centeredColumns[c][j];
        if (k == 1) return double.PositiveInfinity;
        if (k == 2)
        {
            double a = g[0, 0], b = g[0, 1], d = g[1, 1];
            double tr = a + d;
            double det = a * d - b * b;
            double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
            double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
            if (l2 <= 1e-12) return double.PositiveInfinity;
            return Math.Sqrt(l1 / l2);
        }
        // general: power iteration for max, inverse-ish estimate via smallest pivot
        double maxEig = 0;
        var v = new double[k];
        v[0] = 1;
        for (int it = 0; it < 200; it++)
        {
            var w = new double[k];
            for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++) w[i] += g[i, j] * v[j];
            double norm = Math.Sqrt(w.Sum(x => x * x));
            if (norm < 1e-300) break;
            for (int i = 0; i < k; i++) v[i] = w[i] / norm;
            maxEig = norm;
        }
        double minEig = double.MaxValue;
        for (int s = 0; s < k; s++)
        {
            var u = new double[k];
            u[s] = 1;
            for (int it = 0; it < 200; it++)
            {
                var w = Solve(g, u);
                double norm = Math.Sqrt(w.Sum(x => x * x));
                if (norm < 1e-300) break;
                for (int i = 0; i < k; i++) u[i] = w[i] / norm;
            }
            var gu = new double[k];
            for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++) gu[i] += g[i, j] * u[j];
            double eig = 0;
            for (int i = 0; i < k; i++) eig += u[i] * gu[i];
            minEig = Math.Min(minEig, Math.Abs(eig));
        }
        if (minEig <= 1e-12) return double.PositiveInfinity;
        return Math.Sqrt(maxEig / minEig);
    }
}

public record EndmemberInput(string Id, string Name, double[] Values);

public record MixingInput(
    string SampleId,
    double[] Sample,
    double[,] Covariance,
    EndmemberInput[] Endmembers,
    double[][][]? PriorIntervals, // per endmember: allowed [lo,hi] intervals; null entry = [0,1]
    double Chi2Threshold,
    int GridResolution);

public record Candidate(
    int Index,
    double[] Centroid,
    double MinD2,
    List<int[]> Cells); // grid coordinates (i,j) for n=3, (i,0) for n=2

public record MixingResult(
    string Status,
    string[] Flags,
    double ConditionNumber,
    double MinD2Overall,
    double[]? UnconstrainedP,
    List<Candidate> Candidates,
    int GridResolution);

public static class MixingModel
{
    public const string Ok = "OK";
    public const string IdentifiabilityInsufficient = "IDENTIFIABILITY_INSUFFICIENT";
    public const string SampleOutsideHull = "SAMPLE_OUTSIDE_HULL";
    public const string InfeasiblePrior = "INFEASIBLE_PRIOR";
    public const string ToleranceExceeded = "TOLERANCE_EXCEEDED";
    public const string MissingIsotopeDim = "MISSING_ISOTOPE_DIM";

    public const double ConditionThreshold = 1e3;

    public static MixingResult Compute(MixingInput input)
    {
        var flags = new List<string>();
        int n = input.Endmembers.Length;
        int k = input.Sample.Length;
        int res = input.GridResolution;

        // centered endmember columns for conditioning
        var mean = new double[k];
        foreach (var e in input.Endmembers)
            for (int d = 0; d < k; d++) mean[d] += e.Values[d] / n;
        var centered = input.Endmembers
            .Select(e => e.Values.Select((v, d) => v - mean[d]).ToArray())
            .ToArray();
        double cond = LinAlg.ConditionNumber(centered);

        bool underdetermined = k < n - 1;
        bool nearCollinear = cond > ConditionThreshold;
        if (underdetermined) flags.Add("underdetermined-dimensions");
        if (nearCollinear) flags.Add("near-collinear-endmembers");

        var cInv = LinAlg.Inverse(input.Covariance);

        // Endmember matrix E (k x n)
        double D2(double[] p)
        {
            var r = new double[k];
            for (int d = 0; d < k; d++)
            {
                double acc = -input.Sample[d];
                for (int c = 0; c < n; c++) acc += input.Endmembers[c].Values[d] * p[c];
                r[d] = acc;
            }
            // correlated errors propagate through the full covariance (never diagonal-only)
            double d2 = 0;
            for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++)
                    d2 += r[i] * cInv[i, j] * r[j];
            return d2;
        }

        bool PriorOk(double[] p)
        {
            if (input.PriorIntervals is null) return true;
            for (int c = 0; c < n; c++)
            {
                var intervals = input.PriorIntervals[c];
                if (intervals is null || intervals.Length == 0) continue;
                bool inside = intervals.Any(iv => p[c] >= iv[0] - 1e-12 && p[c] <= iv[1] + 1e-12);
                if (!inside) return false;
            }
            return true;
        }

        bool priorRestrictive = input.PriorIntervals is not null &&
            input.PriorIntervals.Any(ivs => ivs is not null && ivs.Length > 0 &&
                ivs.Any(iv => iv[0] > 1e-12 || iv[1] < 1 - 1e-12));

        // Enumerate the simplex directly: feasibility is only ever decided for points
        // that already satisfy p_i >= 0 and sum(p) = 1. Nothing is clipped or
        // renormalized after the fact.
        var cells = new List<(int i, int j, double[] p, double d2, bool feasible)>();
        if (n == 3)
        {
            for (int i = 0; i <= res; i++)
                for (int j = 0; j <= res - i; j++)
                {
                    var p = new[] { (double)i / res, (double)j / res, (double)(res - i - j) / res };
                    double d2 = D2(p);
                    cells.Add((i, j, p, d2, d2 <= input.Chi2Threshold && PriorOk(p)));
                }
        }
        else if (n == 2)
        {
            for (int i = 0; i <= res; i++)
            {
                var p = new[] { (double)i / res, 1.0 - (double)i / res };
                double d2 = D2(p);
                cells.Add((i, 0, p, d2, d2 <= input.Chi2Threshold && PriorOk(p)));
            }
        }
        else
        {
            throw new NotSupportedException("only 2 or 3 endmembers supported");
        }

        var feasible = cells.Where(c => c.feasible).ToList();
        double minD2 = cells.Min(c => c.d2);

        // unconstrained (equality-only) least squares to locate the sample vs hull
        var etce = new double[n, n];
        var etcs = new double[n];
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
                for (int i = 0; i < k; i++)
                    for (int j = 0; j < k; j++)
                        etce[a, b] += input.Endmembers[a].Values[i] * cInv[i, j] * input.Endmembers[b].Values[j];
            for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++)
                    etcs[a] += input.Endmembers[a].Values[i] * cInv[i, j] * input.Sample[j];
        }
        var kkt = new double[n + 1, n + 1];
        var rhs = new double[n + 1];
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++) kkt[a, b] = etce[a, b];
            kkt[a, n] = 1;
            kkt[n, a] = 1;
            rhs[a] = etcs[a];
        }
        rhs[n] = 1;
        double[]? pStar = null;
        try { pStar = LinAlg.Solve(kkt, rhs)[..n]; } catch { }

        // connected components of feasible cells on the triangular/line lattice
        var candidates = new List<Candidate>();
        if (feasible.Count > 0)
        {
            var byKey = feasible.ToDictionary(c => (c.i, c.j), c => c);
            var seen = new HashSet<(int, int)>();
            foreach (var start in feasible)
            {
                if (!seen.Add((start.i, start.j))) continue;
                var comp = new List<(int i, int j, double[] p, double d2, bool feasible)>();
                var queue = new Queue<(int i, int j)>();
                queue.Enqueue((start.i, start.j));
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    var cell = byKey[cur];
                    comp.Add(cell);
                    (int di, int dj)[] neigh = n == 3
                        ? new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, -1), (-1, 1) }
                        : new[] { (1, 0), (-1, 0) };
                    foreach (var (di, dj) in neigh)
                    {
                        var key = (cur.i + di, cur.j + dj);
                        if (byKey.ContainsKey(key) && seen.Add(key)) queue.Enqueue(key);
                    }
                }
                var centroid = new double[n];
                foreach (var c in comp)
                    for (int t = 0; t < n; t++) centroid[t] += c.p[t] / comp.Count;
                candidates.Add(new Candidate(
                    candidates.Count,
                    centroid,
                    comp.Min(c => c.d2),
                    comp.Select(c => new[] { c.i, c.j }).ToList()));
            }
            candidates = candidates.OrderByDescending(c => c.Cells.Count).ToList();
            for (int i = 0; i < candidates.Count; i++)
                candidates[i] = candidates[i] with { Index = i };
            if (candidates.Count > 1) flags.Add("multiple-feasible-regions");
        }

        string status;
        if (underdetermined || nearCollinear)
            status = IdentifiabilityInsufficient;
        else if (feasible.Count == 0)
        {
            bool outsideHull = pStar is not null && pStar.Any(x => x < -1e-9);
            if (outsideHull) status = SampleOutsideHull;
            else if (priorRestrictive) status = InfeasiblePrior;
            else status = ToleranceExceeded;
        }
        else status = Ok;

        return new MixingResult(status, flags.ToArray(), cond, minD2, pStar, candidates, res);
    }
}

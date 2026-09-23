namespace IsotopeStudio;

// ---------- 线性代数小工具（小维度，确定性实现） ----------
public static class LinAlg
{
    public static double[][] Inverse(double[][] a)
    {
        int n = a.Length;
        var m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[2 * n];
            for (int j = 0; j < n; j++) m[i][j] = a[i][j];
            m[i][n + i] = 1.0;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r][col]) > Math.Abs(m[piv][col])) piv = r;
            if (Math.Abs(m[piv][col]) < 1e-12)
                throw new InvalidOperationException("matrix.singular");
            (m[col], m[piv]) = (m[piv], m[col]);
            double d = m[col][col];
            for (int j = 0; j < 2 * n; j++) m[col][j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r][col];
                for (int j = 0; j < 2 * n; j++) m[r][j] -= f * m[col][j];
            }
        }
        var inv = new double[n][];
        for (int i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            for (int j = 0; j < n; j++) inv[i][j] = m[i][n + j];
        }
        return inv;
    }

    // 2 x K 端元矩阵（已中心化）的最小奇异值：sqrt(min eigenvalue of A A^T)
    public static double MinSingularValue2D(double[][] centeredRows)
    {
        double s00 = 0, s01 = 0, s11 = 0;
        foreach (var row in centeredRows)
        {
            s00 += row[0] * row[0];
            s01 += row[0] * row[1];
            s11 += row[1] * row[1];
        }
        double tr = s00 + s11;
        double det = s00 * s11 - s01 * s01;
        double disc = Math.Max(0.0, tr * tr - 4 * det);
        double lamMin = (tr - Math.Sqrt(disc)) / 2.0;
        return Math.Sqrt(Math.Max(0.0, lamMin));
    }

    public static double Mahalanobis2(double[] diff, double[][] covInv)
    {
        int n = diff.Length;
        double d2 = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                d2 += diff[i] * covInv[i][j] * diff[j];
        return d2;
    }
}

// ---------- 引擎输入输出 ----------
public sealed record EndmemberInput(string Id, string Name, double[] Mean, double[][] Cov);
public sealed record SampleMode(double[] Value, double[][] Cov);
public sealed record PriorBounds(double[] Lo, double[] Hi);

public sealed class AnalysisRequest
{
    public required SampleMode[] Modes { get; init; }          // 多样品测定模式（可支持多峰观测）
    public required EndmemberInput[] Endmembers { get; init; }
    public required double[] FractionationOffset { get; init; } // 分馏修正，按同位素维
    public PriorBounds? Prior { get; init; }
    public double Chi2Threshold { get; init; } = 5.99;          // 95%, df=2
    public int GridN { get; init; } = 40;
    public double CollinearityThreshold { get; init; } = 0.5;   // ‰
}

public sealed record FeasibleCell(double[] P, double D2);
public sealed record CandidateRegion(string Id, double[] Centroid, int CellCount,
    double[] MinP, double[] MaxP, double BestD2);

public sealed class AnalysisResult
{
    public required string Status { get; init; } // ok | identifiability-insufficient | outside-envelope | prior-infeasible | empty
    public List<string> Flags { get; } = new();
    public List<string> Diagnostics { get; } = new();
    public List<CandidateRegion> Candidates { get; } = new();
    public List<FeasibleCell> Cells { get; } = new();
    public double SigmaMin { get; set; }
    public bool OutsideEnvelope { get; set; }
    public double[]? Residual { get; set; }      // 最优可行点的残差（样品 - 混合值）
    public double[]? BestProportions { get; set; }
}

public static class MixingEngine
{
    public static AnalysisResult Run(AnalysisRequest req)
    {
        var endmembers = req.Endmembers;
        int k = endmembers.Length;
        var flags = new List<string>();
        var diagnostics = new List<string>();

        // 缺失同位素维：样品维度少于端元维度时，按样品可用维计算并记录诊断
        int sampleDims = req.Modes[0].Value.Length;
        int fullDims = endmembers[0].Mean.Length;
        int dims = Math.Min(sampleDims, fullDims);
        if (sampleDims < fullDims)
            diagnostics.Add($"dimension.missing: sample provides {sampleDims}/{fullDims} isotope dimensions; derived layer reduces to available axes");

        // 分馏修正作用于派生层（原始端元记录不变）
        var means = endmembers.Select(e =>
        {
            var m = new double[dims];
            for (int d = 0; d < dims; d++) m[d] = e.Mean[d] + req.FractionationOffset[d];
            return m;
        }).ToArray();
        var covs = endmembers.Select(e =>
        {
            var c = new double[dims][];
            for (int i = 0; i < dims; i++)
            {
                c[i] = new double[dims];
                for (int j = 0; j < dims; j++) c[i][j] = e.Cov[i][j];
            }
            return c;
        }).ToArray();

        // 近共线检测（2 维同位素空间）
        double sigmaMin = double.PositiveInfinity;
        if (dims == 2)
        {
            var centroid = new double[2];
            foreach (var m in means) { centroid[0] += m[0] / k; centroid[1] += m[1] / k; }
            var centered = means.Select(m => new[] { m[0] - centroid[0], m[1] - centroid[1] }).ToArray();
            sigmaMin = LinAlg.MinSingularValue2D(centered);
            if (sigmaMin < req.CollinearityThreshold)
                flags.Add("identifiability.insufficient");
        }

        // 样品超出端元包络（2 维，对主测定模式）
        bool outside = false;
        if (dims == 2 && k >= 3)
        {
            outside = !PointInConvexHull(req.Modes[0].Value, means);
            if (outside) flags.Add("sample.outside-envelope");
        }

        // 单纯形网格：比例和为一且非负由构造保证，绝不做截断后归一化
        var cells = new List<(int[] grid, double[] p)>();
        var grid = new int[k];
        EnumerateSimplex(req.GridN, k, 0, grid, cells);

        var feasible = new List<FeasibleCell>();
        bool priorExcludedAll = req.Prior is not null;
        foreach (var (g, p) in cells)
        {
            if (req.Prior is not null)
            {
                bool inside = true;
                for (int i = 0; i < k; i++)
                    if (p[i] < req.Prior.Lo[i] - 1e-12 || p[i] > req.Prior.Hi[i] + 1e-12) { inside = false; break; }
                if (!inside) continue;
                priorExcludedAll = false;
            }
            var mixMean = new double[dims];
            var mixCov = new double[dims][];
            for (int i = 0; i < dims; i++) mixCov[i] = new double[dims];
            for (int i = 0; i < k; i++)
            {
                for (int d = 0; d < dims; d++) mixMean[d] += p[i] * means[i][d];
                // 相关误差按协方差传播：C_mix = Σ p_i^2 Σ_i
                for (int a = 0; a < dims; a++)
                    for (int b = 0; b < dims; b++)
                        mixCov[a][b] += p[i] * p[i] * covs[i][a][b];
            }
            double bestD2 = double.PositiveInfinity;
            foreach (var mode in req.Modes)
            {
                var total = new double[dims][];
                for (int a = 0; a < dims; a++)
                {
                    total[a] = new double[dims];
                    for (int b = 0; b < dims; b++) total[a][b] = mixCov[a][b] + mode.Cov[a][b];
                }
                double[][] inv;
                try { inv = LinAlg.Inverse(total); }
                catch (InvalidOperationException)
                {
                    diagnostics.Add("covariance.singular: total covariance not invertible at some grid points");
                    continue;
                }
                var diff = new double[dims];
                for (int d = 0; d < dims; d++) diff[d] = mixMean[d] - mode.Value[d];
                bestD2 = Math.Min(bestD2, LinAlg.Mahalanobis2(diff, inv));
            }
            if (bestD2 <= req.Chi2Threshold) feasible.Add(new FeasibleCell(p, bestD2));
        }

        var result = new AnalysisResult { Status = "ok" };
        foreach (var f in flags) result.Flags.Add(f);
        foreach (var d in diagnostics) result.Diagnostics.Add(d);
        result.SigmaMin = sigmaMin;
        result.OutsideEnvelope = outside;

        if (feasible.Count == 0)
        {
            result.Status = priorExcludedAll || req.Prior is not null ? "prior-infeasible" : "empty";
            if (result.Status == "prior-infeasible") result.Flags.Add("prior.infeasible");
            return result;
        }

        // 连通分量 => 多个可行区各自成为候选，全部保留
        var components = ConnectedComponents(feasible, req.GridN, k);
        int idx = 0;
        foreach (var comp in components.OrderByDescending(c => c.Count))
        {
            idx++;
            var centroid = new double[k];
            double bestD2 = double.PositiveInfinity;
            var minP = Enumerable.Repeat(double.PositiveInfinity, k).ToArray();
            var maxP = Enumerable.Repeat(double.NegativeInfinity, k).ToArray();
            foreach (var cell in comp)
            {
                for (int i = 0; i < k; i++)
                {
                    centroid[i] += cell.P[i] / comp.Count;
                    minP[i] = Math.Min(minP[i], cell.P[i]);
                    maxP[i] = Math.Max(maxP[i], cell.P[i]);
                }
                bestD2 = Math.Min(bestD2, cell.D2);
            }
            result.Candidates.Add(new CandidateRegion($"cand-{idx}", centroid, comp.Count, minP, maxP, bestD2));
        }
        foreach (var c in feasible) result.Cells.Add(c);

        var best = feasible.OrderBy(c => c.D2).First();
        result.BestProportions = best.P;
        var residual = new double[dims];
        for (int d = 0; d < dims; d++)
        {
            double mix = 0;
            for (int i = 0; i < k; i++) mix += best.P[i] * means[i][d];
            residual[d] = req.Modes[0].Value[d] - mix;
        }
        result.Residual = residual;

        if (result.Flags.Contains("identifiability.insufficient"))
            result.Status = "identifiability-insufficient";
        else if (outside)
            result.Status = "outside-envelope";
        return result;
    }

    private static void EnumerateSimplex(int remaining, int k, int idx, int[] grid,
        List<(int[] grid, double[] p)> cells)
    {
        if (idx == k - 1)
        {
            grid[idx] = remaining;
            var gcopy = (int[])grid.Clone();
            var p = grid.Select(g => (double)g).ToArray();
            int total = p.Sum() > 0 ? (int)p.Sum() : 1;
            for (int i = 0; i < p.Length; i++) p[i] /= total;
            cells.Add((gcopy, p));
            return;
        }
        for (int v = 0; v <= remaining; v++)
        {
            grid[idx] = v;
            EnumerateSimplex(remaining - v, k, idx + 1, grid, cells);
        }
    }

    private static List<List<FeasibleCell>> ConnectedComponents(List<FeasibleCell> cells, int n, int k)
    {
        // 用网格坐标做邻居判定：两点在恰好两个坐标上各差一个网格单位
        var key = new Dictionary<string, FeasibleCell>();
        foreach (var c in cells)
            key[string.Join(',', c.P.Select(x => (int)Math.Round(x * n)))] = c;
        var seen = new HashSet<string>();
        var comps = new List<List<FeasibleCell>>();
        foreach (var kv in key)
        {
            if (seen.Contains(kv.Key)) continue;
            var comp = new List<FeasibleCell>();
            var stack = new Stack<string>();
            stack.Push(kv.Key);
            seen.Add(kv.Key);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                var coords = cur.Split(',').Select(int.Parse).ToArray();
                comp.Add(key[cur]);
                for (int i = 0; i < k; i++)
                    for (int j = 0; j < k; j++)
                    {
                        if (i == j) continue;
                        var nb = (int[])coords.Clone();
                        nb[i]++; nb[j]--;
                        if (nb[j] < 0) continue;
                        var nk = string.Join(',', nb);
                        if (key.ContainsKey(nk) && seen.Add(nk)) stack.Push(nk);
                    }
            }
            comps.Add(comp);
        }
        return comps;
    }

    public static bool PointInConvexHull(double[] point, double[][] vertices)
    {
        // 单调链凸包 + 射线法（2D）
        var pts = vertices.Select(v => (x: v[0], y: v[1])).OrderBy(p => p.x).ThenBy(p => p.y).ToList();
        if (pts.Count < 3) return false;
        double Cross((double x, double y) o, (double x, double y) a, (double x, double y) b)
            => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var lower = new List<(double x, double y)>();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double x, double y)>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        var hull = lower.Concat(upper).ToList();
        bool inside = false;
        for (int i = 0, j = hull.Count - 1; i < hull.Count; j = i++)
        {
            if ((hull[i].y > point[1]) != (hull[j].y > point[1]) &&
                point[0] < (hull[j].x - hull[i].x) * (point[1] - hull[i].y) / (hull[j].y - hull[i].y) + hull[i].x)
                inside = !inside;
        }
        return inside;
    }
}

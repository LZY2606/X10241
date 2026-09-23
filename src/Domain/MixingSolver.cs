namespace IsotopeMix.Domain;

/// <summary>一次混合分析的规范化输入（派生层；原始记录在来源层）。</summary>
public sealed record MixingProblem
{
    public required string ProblemId { get; init; }
    public required IReadOnlyList<string> Dimensions { get; init; }
    public required IReadOnlyList<string> EndmemberIds { get; init; }
    public required double[] SampleMean { get; init; }
    public required double[,] SampleCovariance { get; init; }
    /// <summary>每个端元的模式；第 k 个元素的第 j 个模式为 (modeId, 均值, 协方差)。</summary>
    public required IReadOnlyList<IReadOnlyList<(string ModeId, string Label, double[] Mean, double[,] Cov)>> EndmemberModes { get; init; }
    public required double[,] FractionationOffsets { get; init; }
    public required IReadOnlyList<(double? Min, double? Max)> PriorBounds { get; init; }
    public SolverOptions Options { get; init; } = new();
}

public static class MixingModelSolver
{
    public const string SolverVersion = "iso-mix-solver-1.0.0";

    public static SolverResult Solve(MixingProblem problem)
    {
        var p = problem;
        int m = p.EndmemberIds.Count;
        if (m is < 2 or > 4)
            throw new ArgumentException("grid feasibility solver supports 2-4 endmembers", nameof(problem));

        var diagnostics = new List<string>();
        int d = p.Dimensions.Count;

        // ---- 设计矩阵：列 = 分馏修正后的端元中心（用每个端元的第一模式做秩判定）----
        var design = new double[d, m];
        for (int k = 0; k < m; k++)
        {
            var mode = p.EndmemberModes[k][0];
            for (int i = 0; i < d; i++)
                design[i, k] = mode.Mean[i] + p.FractionationOffsets[k, i];
        }
        // 和为一约束提升秩判定：增广一行常数
        var augmented = AugmentSumToOne(design);
        var svd = LinearAlgebra.SvdViaNormalEquations(augmented);
        bool rankDeficient = svd.Rank < m;
        bool nearCollinear = !rankDeficient && svd.Condition > p.Options.IdentifiabilityConditionThreshold;

        double critical = Statistics.ChiSquareCritical(p.Options.ChiSquareLevel, d);

        // ---- 模式组合枚举（多个可行区的合法来源，不允许"只保留一个最优模式"）----
        var combos = EnumerateModeCombos(p.EndmemberModes);

        var boundFeasible = new bool[m];
        bool priorEmpty = false;
        for (int k = 0; k < m; k++)
        {
            double lo = p.PriorBounds[k].Min ?? 0;
            double hi = p.PriorBounds[k].Max ?? 1;
            if (lo > hi || lo > 1 + 1e-9 || hi < -1e-9) priorEmpty = true;
            boundFeasible[k] = lo <= 1 + 1e-9 && hi >= -1e-9;
        }

        var grid = EnumerateSimplex(m, p.Options.GridResolution);
        var labels = new int[grid.Count]; // 0 = 不可行，>0 = 连通分量号
        var pointStats = new FeasiblePoint?[grid.Count];
        string[] comboAt = new string[grid.Count];
        // feasibleCombos[i]：格点 i 上可行的全部模式组合（多候选全部保留）
        var feasibleCombos = new HashSet<string>[grid.Count];
        for (int i = 0; i < grid.Count; i++) feasibleCombos[i] = new HashSet<string>();

        if (!priorEmpty)
        {
            foreach (var combo in combos)
            {
                if (IsCorrelated(p.SampleCovariance) || combo.Covs.Any(IsCorrelated))
                {
                    if (!diagnostics.Any(x => x.StartsWith("CORRELATED_ERRORS")))
                        diagnostics.Add("CORRELATED_ERRORS: 测量/端元协方差含非零相关项，按完整协方差矩阵传播并做 Mahalanobis 检验");
                }
                for (int i = 0; i < grid.Count; i++)
                {
                    if (!WithinBounds(grid[i], p.PriorBounds)) continue;
                    double chi2 = Chi2At(grid[i], p, combo.Means, combo.Covs);
                    if (chi2 <= critical) feasibleCombos[i].Add(combo.Id);
                }
            }
            var feasible = feasibleCombos.Select(set => set.Count > 0).ToArray();
            int nextLabel = 1;
            foreach (int seed in Enumerable.Range(0, grid.Count).Where(i => feasible[i] && labels[i] == 0))
            {
                var comp = FloodComponent(seed, grid, p, feasibleCombos);
                if (comp.Count == 0) continue;
                int existing = comp.Where(qi => labels[qi] > 0).Select(qi => labels[qi]).DefaultIfEmpty(0).Min();
                int componentId = existing == 0 ? nextLabel++ : existing;
                foreach (int qi in comp)
                {
                    labels[qi] = componentId;
                    string comboId = feasibleCombos[qi].OrderBy(cid =>
                        Chi2At(grid[qi], p, ComboById(combos, cid).Means, ComboById(combos, cid).Covs)).First();
                    comboAt[qi] = comboId;
                    var chosen = ComboById(combos, comboId);
                    pointStats[qi] = EvaluatePoint(grid[qi], p.SampleMean, p.SampleCovariance,
                        chosen.Means, chosen.Covs, p.FractionationOffsets, comboId);
                }
            }
        }
        else
        {
            diagnostics.Add("PRIOR_INFEASIBLE: 先验区间为空（下限>上限或完全落在单纯形之外），不存在合法比例");
        }

        // ---- 连通分量几何 ----
        var components = BuildComponents(labels, pointStats, grid, comboAt, m, p.Options.GridResolution);

        // ---- 最优点（可行内）与无约束残差点（先验不可行/超包络时仍展示）----
        FeasiblePoint? best = components.SelectMany(c => new[] { c.Best }).OrderBy(x => x.ChiSquare).FirstOrDefault();
        bool outside = IsOutsideEnvelope(p, diagnostics, d, m);
        if (best is null)
        {
            var anyCombo = combos[0];
            int nearest = grid
                .Select((f, i) => (i, chi2: Chi2At(f, p, anyCombo.Means, anyCombo.Covs)))
                .OrderBy(x => x.chi2).First().i;
            best = EvaluatePoint(grid[nearest], p.SampleMean, p.SampleCovariance,
                anyCombo.Means, anyCombo.Covs, p.FractionationOffsets, anyCombo.Id);
        }

        if (d < m)
        {
            diagnostics.Insert(0, $"MISSING_DIMENSIONS: 有效同位素维 {d} < 端元数 {m}，比例不能被唯一决定，输出为线段/面状可行域");
        }
        if (rankDeficient)
        {
            diagnostics.Insert(0, $"IDENTIFIABILITY_INSUFFICIENT: 设计矩阵秩 {svd.Rank} < 端元数 {m}，端元（近）共线，无法唯一决定比例");
        }
        else if (nearCollinear)
        {
            diagnostics.Insert(0, $"IDENTIFIABILITY_WEAK: 条件数 {svd.Condition:G3} 超过阈值 {p.Options.IdentifiabilityConditionThreshold}，端元近共线，可识别性不足");
        }
        if (componentCount > 1)
            diagnostics.Add($"MULTIPLE_FEASIBLE_REGIONS: 存在 {componentCount} 个互不连通的合法可行区，禁止只输出单点比例");
        if (componentCount == 0 && !priorEmpty)
            diagnostics.Add("NO_FEASIBLE_REGION: 在观测协方差传播后的置信域内没有满足和为一且非负的比例（样品可能超出端元包络）");

        var paramCov = best is null
            ? new Dictionary<string, double>()
            : CovMap(Statistics.ParameterCovariance(design, PredictedCov(best.BestFractions, p, combos[0].Means, combos[0].Covs)), p.EndmemberIds);

        return new SolverResult
        {
            Identifiability = rankDeficient ? "insufficient" : nearCollinear ? "weak" : d < m ? "underdetermined" : "identified",
            Diagnostics = diagnostics,
            UsedDimensions = p.Dimensions,
            EndmemberOrder = p.EndmemberIds,
            ConditionNumber = svd.Condition,
            DesignMatrix = design,
            NullspaceDirections = svd.Nullspace,
            Components = components,
            BestPoint = best,
            ChiSquareCritical = critical,
            PriorInfeasible = priorEmpty,
            SampleOutsideEnvelope = outside,
            GridResolution = p.Options.GridResolution,
            ParameterCovarianceAtBest = paramCov
        };
    }

    private static double[,] AugmentSumToOne(double[,] design)
    {
        int d = design.GetLength(0), m = design.GetLength(1);
        var a = new double[d + 1, m];
        for (int i = 0; i < d; i++)
            for (int k = 0; k < m; k++)
                a[i, k] = design[i, k];
        for (int k = 0; k < m; k++) a[d, k] = 1.0;
        return a;
    }

    private static bool IsOutsideEnvelope(MixingProblem p, List<string> diagnostics, int d, int m)
    {
        bool insideAnyModeCombo = false;
        bool nearEdge = false;
        foreach (var combo in EnumerateModeCombos(p.EndmemberModes))
        {
            if (ConvexHull.Contains(combo.Means, p.SampleMean, d))
            {
                insideAnyModeCombo = true;
                if (DistanceToHullBoundary(combo.Means, p.SampleMean, d) < 0.05) nearEdge = true;
            }
        }
        if (!insideAnyModeCombo)
        {
            diagnostics.Insert(0, "SAMPLE_OUTSIDE_ENVELOPE: 样品均值位于全部端元模式凸包包络之外");
            return true;
        }
        if (nearEdge)
            diagnostics.Add("NEAR_ENVELOPE_EDGE: 样品靠近包络边界，至少一个比例在 0 附近，结论对测量噪声敏感");
        return false;
    }

    private static double DistanceToHullBoundary(IReadOnlyList<double[]> hull, double[] point, int d)
    {
        if (d != 2) return 0;
        var poly = ConvexHull.In2D(hull.Select(p => new[] { p[0], p[1] }).ToList());
        double best = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            double vx = b[0] - a[0], vy = b[1] - a[1];
            double len2 = vx * vx + vy * vy;
            double t = len2 < 1e-12 ? 0 : Math.Clamp(((point[0] - a[0]) * vx + (point[1] - a[1]) * vy) / len2, 0, 1);
            double px = a[0] + t * vx, py = a[1] + t * vy;
            best = Math.Min(best, Math.Sqrt((point[0] - px) * (point[0] - px) + (point[1] - py) * (point[1] - py)));
        }
        return best;
    }

    private static Dictionary<string, double> CovMap(double[,] cov, IReadOnlyList<string> ids)
    {
        var map = new Dictionary<string, double>();
        for (int i = 0; i < ids.Count; i++)
            for (int j = 0; j < ids.Count; j++)
                map[$"{ids[i]}|{ids[j]}"] = cov[i, j];
        return map;
    }

    private static double[,] PredictedCov(double[] f, MixingProblem p,
        IReadOnlyList<double[]> means, IReadOnlyList<double[,]> covs)
        => Statistics.PredictCovariance(p.SampleCovariance, covs, f);

    // ---------- 网格 ----------

    public static List<double[]> EnumerateSimplex(int m, int resolution)
    {
        var points = new List<double[]>();
        if (m == 2)
        {
            for (int i = 0; i <= resolution; i++)
            {
                double a = (double)i / resolution;
                points.Add(new[] { a, 1 - a });
            }
            return points;
        }
        if (m == 3)
        {
            for (int i = 0; i <= resolution; i++)
                for (int j = 0; j <= resolution - i; j++)
                {
                    double f1 = (double)i / resolution;
                    double f2 = (double)j / resolution;
                    points.Add(new[] { f1, f2, 1 - f1 - f2 });
                }
            return points;
        }
        // m == 4：分辨率自动收紧，避免格点爆炸（41 -> 15）
        int r = Math.Min(resolution, 16);
        for (int i = 0; i <= r; i++)
            for (int j = 0; j <= r - i; j++)
                for (int k = 0; k <= r - i - j; k++)
                {
                    double f1 = (double)i / r, f2 = (double)j / r, f3 = (double)k / r;
                    points.Add(new[] { f1, f2, f3, 1 - f1 - f2 - f3 });
                }
        return points;
    }

    private sealed record ModeCombo(string Id, IReadOnlyList<double[]> Means, IReadOnlyList<double[,]> Covs, IReadOnlyList<string> ModeIds);

    private static List<ModeCombo> EnumerateModeCombos(
        IReadOnlyList<IReadOnlyList<(string ModeId, string Label, double[] Mean, double[,] Cov)>> modes)
    {
        var combos = new List<ModeCombo>();
        int m = modes.Count;
        int[] picks = new int[m];
        while (true)
        {
            var means = new double[m][];
            var covs = new double[m][,];
            var ids = new string[m];
            for (int k = 0; k < m; k++)
            {
                var mode = modes[k][picks[k]];
                means[k] = mode.Mean;
                covs[k] = mode.Cov;
                ids[k] = mode.ModeId;
            }
            combos.Add(new ModeCombo(string.Join("+", ids), means, covs, ids));
            int t = 0;
            while (t < m && picks[t] == modes[t].Count - 1) { picks[t] = 0; t++; }
            if (t == m) break;
            picks[t]++;
        }
        return combos;
    }

    private static bool IsCorrelated(double[,] c)
    {
        int d = c.GetLength(0);
        for (int i = 0; i < d; i++)
            for (int j = i + 1; j < d; j++)
                if (Math.Abs(c[i, j]) > 1e-12 * Math.Sqrt(c[i, i] * c[j, j]))
                    return true;
        return false;
    }

    private static bool WithinBounds(double[] f, IReadOnlyList<(double? Min, double? Max)> bounds)
    {
        for (int k = 0; k < f.Length; k++)
        {
            if (bounds[k].Min is double lo && f[k] < lo - 1e-9) return false;
            if (bounds[k].Max is double hi && f[k] > hi + 1e-9) return false;
        }
        return true;
    }

    private static double Chi2At(double[] f, MixingProblem p,
        IReadOnlyList<double[]> means, IReadOnlyList<double[,]> covs)
    {
        int d = p.Dimensions.Count;
        var predicted = new double[d];
        for (int k = 0; k < f.Length; k++)
            for (int i = 0; i < d; i++)
                predicted[i] += f[k] * (means[k][i] + p.FractionationOffsets[k, i]);
        var residual = p.SampleMean.Zip(predicted, (a, b) => a - b).ToArray();
        var covariance = Statistics.PredictCovariance(p.SampleCovariance, covs, f);
        return Statistics.MahalanobisSquare(residual, covariance);
    }

    private static FeasiblePoint EvaluatePoint(double[] f, double[] sample, double[,] sampleCov,
        IReadOnlyList<double[]> means, IReadOnlyList<double[,]> covs,
        double[,] fractionation, string comboId)
    {
        int d = sample.Length, m = f.Length;
        var predicted = new double[d];
        for (int k = 0; k < m; k++)
            for (int i = 0; i < d; i++)
                predicted[i] += f[k] * (means[k][i] + fractionation[k, i]);
        var residual = sample.Zip(predicted, (a, b) => a - b).ToArray();
        var covariance = Statistics.PredictCovariance(sampleCov, covs, f);
        double chi2 = Statistics.MahalanobisSquare(residual, covariance);
        return new FeasiblePoint
        {
            Fractions = f,
            ChiSquare = chi2,
            Residuals = residual,
            StandardizedResiduals = Statistics.Standardized(residual, covariance),
            ModeComboId = comboId
        };
    }

    private static readonly int[][] NeighborDeltas3 =
    {
        new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 },
        new[] { 1, -1 }, new[] { -1, 1 }
    };

    private static List<int> FloodComponent(int seed, List<double[]> grid, MixingProblem p,
        HashSet<string>[] feasibleCombos)
    {
        int m = p.EndmemberIds.Count;
        int r = m == 4 ? Math.Min(p.Options.GridResolution, 16) : p.Options.GridResolution;
        var comp = new List<int>();
        var seen = new HashSet<int> { seed };
        var stack = new Stack<int>();
        stack.Push(seed);
        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            comp.Add(idx);
            foreach (var n in Neighbors(idx, m, r))
            {
                if (n < 0 || seen.Contains(n) || feasibleCombos[n].Count == 0) continue;
                if (!feasibleCombos[idx].Overlaps(feasibleCombos[n])) continue;
                seen.Add(n);
                stack.Push(n);
            }
        }
        return comp;
    }

    private static ModeCombo ComboById(List<ModeCombo> combos, string id)
        => combos.First(c => c.Id == id);

    /// <summary>格点 i 的三角网格邻居（按 EnumerateSimplex 的生成顺序反解坐标）。</summary>
    private static IEnumerable<int> Neighbors(int idx, int m, int r)
    {
        if (m == 2)
        {
            if (idx > 0) yield return idx - 1;
            if (idx < r) yield return idx + 1;
            yield break;
        }
        if (m == 3)
        {
            int a = 0, start = 0;
            for (int i = 0; i <= r; i++)
            {
                int rowLen = r - i + 1;
                if (idx < start + rowLen) { a = i; break; }
                start += rowLen;
            }
            int b = idx - start;
            foreach (var d in NeighborDeltas3)
            {
                int na = a + d[0], nb = b + d[1];
                if (na < 0 || nb < 0 || na + nb > r) continue;
                yield return Index3(na, nb, r);
            }
            yield break;
        }
        // m == 4：仅沿四个坐标轴向邻居
        var coords = IndexTo4(idx, r);
        foreach (var d in new[] { 1, -1 })
            for (int axis = 0; axis < 4; axis++)
            {
                var nc = (int[])coords.Clone();
                nc[axis] += d;
                if (nc.Any(v => v < 0) || nc.Sum() != r) continue;
                yield return Index4(nc, r);
            }
    }

    private static int Index3(int a, int b, int r)
    {
        int start = 0;
        for (int i = 0; i < a; i++) start += r - i + 1;
        return start + b;
    }

    private static int[] IndexTo4(int idx, int r)
    {
        int start = 0;
        for (int i = 0; i <= r; i++)
        {
            for (int j = 0; j <= r - i; j++)
            {
                int layerLen = r - i - j + 1;
                if (idx < start + layerLen)
                    return new[] { i, j, idx - start, 0 };
                start += layerLen;
            }
        }
        throw new InvalidOperationException();
    }

    private static int Index4(int[] c, int r)
    {
        int idx = 0;
        for (int i = 0; i < c[0]; i++)
            for (int j = 0; j <= r - i; j++)
                idx += r - i - j + 1;
        for (int j = 0; j < c[1]; j++)
            idx += r - c[0] - j + 1;
        return idx + c[2];
    }

    private static List<FeasibleComponent> BuildComponents(
        int[] labels, FeasiblePoint?[] stats, List<double[]> grid, string[] comboAt, int m, int resolution)
    {
        var result = new List<FeasibleComponent>();
        foreach (var group in Enumerable.Range(0, labels.Length)
            .Where(i => labels[i] > 0)
            .GroupBy(i => labels[i]))
        {
            var members = group.ToList();
            var pts = members.Select(i => grid[i]).ToList();
            var centroid = new double[m];
            foreach (var f in pts) for (int k = 0; k < m; k++) centroid[k] += f[k] / pts.Count;
            var best = members.Select(i => stats[i]!).OrderBy(x => x.ChiSquare).First();
            var hull = m switch
            {
                2 => pts.OrderBy(p => p[0]).Select(p => (IReadOnlyList<double>)p).ToList(),
                3 => ConvexHull.In2D(pts.Select(p => new[] { p[0], p[1] }).ToList()),
                _ => pts.Select(p => (IReadOnlyList<double>)p).Take(12).ToList()
            };
            double area = m == 3 ? PolygonArea(hull.Select(q => (q[0], q[1])).ToList()) : (double)members.Count / grid.Count;
            result.Add(new FeasibleComponent
            {
                Index = group.Key,
                Vertices = hull.Select(q => q.ToArray()).ToList(),
                Centroid = centroid,
                Volume = area,
                BestFractions = best.Fractions,
                BestChiSquare = best.ChiSquare,
                ModeComboId = comboAt[members[0]]
            });
        }
        return result.OrderBy(c => c.Index).ToList();
    }

    private static double PolygonArea(List<(double X, double Y)> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return Math.Abs(a) / 2;
    }
}

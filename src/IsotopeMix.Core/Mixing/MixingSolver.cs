using IsotopeMix.Core.LinearAlgebra;
using IsotopeMix.Core.Models;

namespace IsotopeMix.Core.Mixing;

/// <summary>求解参数（属于“规则版本”的一部分）。</summary>
public sealed record SolverSettings(
    string RuleVersion = "rules-2026.09",
    double Confidence = 0.95,
    int GridN = 48,
    double CollinearityRatio = 0.05)
{
    public static readonly SolverSettings Default = new();
}

public sealed record SolverInputs(
    SampleRecord Sample,
    EndmemberDistribution Endmembers,
    FractionationRecord Fractionation,
    CovarianceRecord? SampleCovariance,
    PriorRecord? Prior,
    SolverSettings Settings);

/// <summary>
/// 稳定同位素线性混合可行域求解器。
/// 关键规则：
///  - 相关误差通过协方差矩阵整体传播（不是独立 sigma 相加）；
///  - 非负且和为一的约束用真实单纯形投影，绝不截断负数再归一化；
///  - 端元近共线时显式报 Underidentified，保留全部合法候选；
///  - 先验盒不可行 → InfeasiblePrior；样品落在包络误差域外 → OutsideEnvelope；
///  - 缺失同位素维在降维空间计算并标记 MissingDimension。
/// </summary>
public sealed class MixingSolver
{
    public MixingResult Solve(SolverInputs inputs, string runId)
    {
        var (sample, em, frac, cov, prior, settings) = inputs;
        var diagnostics = new List<string>();
        var result = new MixingResult
        {
            RunId = runId,
            RuleVersion = settings.RuleVersion,
            SampleId = sample.Id,
            EndmemberVersion = $"{em.Id}@{em.VersionLabel}",
            FractionationVersion = $"{frac.Id}@{frac.VersionLabel}",
            CovarianceId = cov?.Id ?? "",
            PriorId = prior?.Id ?? "",
            GridSubdivisions = settings.GridN,
            EndmemberLabels = em.Members.Select(m => m.MemberName).ToList()
        };

        // 维度：以端元声明的同位素维为准，找出样品缺失维。
        var allDims = em.Members
            .SelectMany(m => m.Isotopes.Keys)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        var observedDims = allDims
            .Where(d => sample.Isotopes.TryGetValue(d, out var mv)
                        && mv.CanonicalPermil.HasValue)
            .ToList();
        result.Dimensions = observedDims;
        result.MissingDimensions = allDims.Except(observedDims).ToList();
        if (result.MissingDimensions.Count > 0)
            result.Flags |= MixingFlags.MissingDimension;

        int d = observedDims.Count;
        int k = em.Members.Count;
        if (d == 0 || k < 2)
        {
            result.Diagnosis = "有效同位素维度为零或端元数 < 2，无法建立混合方程";
            result.Diagnostics.Add(result.Diagnosis);
            result.PriorFeasible = CheckPrior(prior, k, out _, out _);
            return result;
        }

        // 设计矩阵 A[d,k]：分馏校正后的端元均值 μ_ij + ε_j。
        var design = new double[d, k];
        var emCov = new double[d, d];
        for (int j = 0; j < k; j++)
        {
            var member = em.Members[j];
            for (int i = 0; i < d; i++)
            {
                var dim = observedDims[i];
                double mean = member.Isotopes.TryGetValue(dim, out var mv) && mv.CanonicalPermil is { } v
                    ? v : 0;
                double offset = frac.OffsetsPermil.TryGetValue(dim, out var off) ? off : 0;
                design[i, j] = mean + offset;
                if (member.Isotopes.TryGetValue(dim, out var m2) && m2.CanonicalSigmaPermil is { } sig)
                {
                    emCov[i, i] += sig * sig;
                }
            }
        }
        // 端元“分布”：混合分布方差按 Σ_j p_j σ_j² 计算，与 p 有关；
        // 网格上逐点加入，这里先保存各端元方差向量。
        var emVarByDim = new double[d, k];
        for (int j = 0; j < k; j++)
        for (int i = 0; i < d; i++)
        {
            var dim = observedDims[i];
            if (em.Members[j].Isotopes.TryGetValue(dim, out var mv) && mv.CanonicalSigmaPermil is { } s)
                emVarByDim[i, j] = s * s;
        }

        var y = new double[d];
        for (int i = 0; i < d; i++)
            y[i] = sample.Isotopes[observedDims[i]].CanonicalPermil!.Value;

        result.DesignMatrix = design;
        result.Observed = y;

        // 样品测量协方差（允许相关误差）。
        double[,]? sampleCov = null;
        if (cov is not null)
        {
            sampleCov = new double[d, d];
            for (int i = 0; i < d; i++)
            for (int j = 0; j < d; j++)
            {
                int ri = cov.Dimensions.IndexOf(observedDims[i]);
                int rj = cov.Dimensions.IndexOf(observedDims[j]);
                if (ri >= 0 && rj >= 0) sampleCov[i, j] = cov.Matrix[ri, rj];
            }
            result.SampleCovariance = sampleCov;
        }
        result.EndmemberCovariance = emCov;
        result.TotalCovariance = sampleCov ?? DiagonalFromObserved(sample, observedDims);

        // 先验盒。
        var lower = new double[k];
        var upper = new double[k];
        bool priorGiven = prior is not null;
        for (int j = 0; j < k; j++)
        {
            var label = em.Members[j].MemberName;
            lower[j] = prior is not null && prior.LowerFraction.TryGetValue(label, out var l) ? l : 0.0;
            upper[j] = prior is not null && prior.UpperFraction.TryGetValue(label, out var u) ? u : 1.0;
        }
        bool priorFeasible = LA.PriorIntersectsSimplex(lower, upper);
        result.PriorFeasible = priorFeasible;
        if (priorGiven && !priorFeasible)
        {
            result.Flags |= MixingFlags.InfeasiblePrior;
            result.Diagnosis = "先验盒约束与单纯形（非负、和为1）不相交：没有任何比例能满足先验";
            result.Diagnostics.Add(result.Diagnosis);
            // 仍做无先验几何诊断，但不产生候选。
        }
        if (frac.OffsetsPermil.Count > 0) result.Flags |= MixingFlags.FractionationApplied;
        if (em.Distributional) result.Flags |= MixingFlags.DistributionalEndmembers;

        // 近共线诊断：中心化后的设计矩阵最小/最大奇异值比。
        var centered = CenterColumns(design, d, k);
        var sv = LA.SingularValues(centered);
        double ratio = sv.Length > 0 && sv[0] > 1e-12 ? sv[^1] / sv[0] : 0;
        result.SingularRatio = ratio;
        bool structurallyShort = d < k - 1;
        if (ratio < settings.CollinearityRatio || structurallyShort)
        {
            result.Flags |= MixingFlags.Underidentified;
            string why = structurallyShort
                ? $"观测维数 {d} 少于端元数−1 ({k - 1})，比例结构性不可唯一识别"
                : $"端元在同位素空间近共线（奇异值比 {ratio:E2} < {settings.CollinearityRatio}）";
            result.Diagnosis = "可识别性不足：" + why + "；下列可行域保留全部合法候选，不给单点答案";
            diagnostics.Add(result.Diagnosis);
        }

        // 无约束最小二乘（仅信息展示；近共线时正规方程奇异 → null）。
        double[]? unconstrained = null;
        try { unconstrained = LA.SolvePositiveDefinite(ATA(design), ATy(design, y)); }
        catch (InvalidOperationException) { unconstrained = null; }
        result.UnconstrainedFit = unconstrained;

        var effLower = priorFeasible ? lower : Zero(k);
        var effUpper = priorFeasible ? upper : Ones(k);
        var best = LA.ConstrainedFit(design, y, effLower, effUpper);
        if (best is not null)
        {
            result.ConstrainedBestFit = best;
            var resid = Residual(design, best, y);
            var covAtBest = TotalAt(sampleCov, emVarByDim, best, d);
            double chi = LA.QuadraticInverse(covAtBest, resid);
            result.BestResiduals = resid;
            result.BestChiSquare = chi;
        }

        double threshold = ChiSquare.Quantile(settings.Confidence, d);
        result.ChiThreshold = threshold;

        bool emitRegions = priorFeasible;
        if (emitRegions)
        {
            var regions = EnumerateRegions(settings.GridN, k, design, y, sampleCov, emVarByDim,
                effLower, effUpper, threshold, d);
            result.Regions = regions;
            if (regions.Count > 1) result.Flags |= MixingFlags.MultipleFeasibleRegions;
            if (regions.Count == 0)
            {
                bool envelope = OutsideGeometricEnvelope(design, y, k, d, priorFeasible ? lower : Zero(k),
                    priorFeasible ? upper : Ones(k));
                if (envelope)
                {
                    result.Flags |= MixingFlags.OutsideEnvelope;
                    result.Diagnosis = Concat(result.Diagnosis,
                        "样品在给定测量协方差下落在端元凸包之外（包络误差域内无可行比例）");
                }
                else
                {
                    result.Diagnosis = Concat(result.Diagnosis,
                        "在当前置信椭圆内没有可行比例，但样品仍在端元几何包络附近");
                }
            }
        }

        result.Diagnostics.AddRange(diagnostics);
        return result;
    }

    private static string? Concat(string? a, string b)
        => string.IsNullOrEmpty(a) ? b : a + "；" + b;

    private static bool CheckPrior(PriorRecord? prior, int k, out double[] lo, out double[] up)
    {
        lo = new double[k]; up = new double[k];
        Array.Fill(up, 1.0);
        return LA.PriorIntersectsSimplex(lo, up);
    }

    private static double[,] DiagonalFromObserved(SampleRecord sample, List<string> dims)
    {
        int d = dims.Count;
        var m = new double[d, d];
        for (int i = 0; i < d; i++)
        {
            double sig = sample.Isotopes[dims[i]].CanonicalSigmaPermil ?? 0.25;
            m[i, i] = sig * sig;
        }
        return m;
    }

    private static double[,] CenterColumns(double[,] a, int d, int k)
    {
        var means = new double[k];
        for (int j = 0; j < k; j++)
        {
            double s = 0;
            for (int i = 0; i < d; i++) s += a[i, j];
            means[j] = s / d;
        }
        var c = new double[d, k];
        for (int i = 0; i < d; i++)
            for (int j = 0; j < k; j++)
                c[i, j] = a[i, j] - means[j];
        return c;
    }

    private static double[,] ATA(double[,] a)
    {
        int d = a.GetLength(0), k = a.GetLength(1);
        var m = new double[k, k];
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++)
            {
                double s = 0;
                for (int r = 0; r < d; r++) s += a[r, i] * a[r, j];
                m[i, j] = s;
            }
        return m;
    }

    private static double[] ATy(double[,] a, double[] y)
    {
        int d = a.GetLength(0), k = a.GetLength(1);
        var m = new double[k];
        for (int j = 0; j < k; j++)
        {
            double s = 0;
            for (int i = 0; i < d; i++) s += a[i, j] * y[i];
            m[j] = s;
        }
        return m;
    }

    private static double[] Residual(double[,] design, double[] p, double[] y)
    {
        int d = design.GetLength(0), k = design.GetLength(1);
        var r = new double[d];
        for (int i = 0; i < d; i++)
        {
            double pred = 0;
            for (int j = 0; j < k; j++) pred += design[i, j] * p[j];
            r[i] = y[i] - pred;
        }
        return r;
    }

    private static double[,] TotalAt(double[,]? sampleCov, double[,] emVar, double[] p, int d)
    {
        var total = new double[d, d];
        if (sampleCov is not null)
            for (int i = 0; i < d; i++)
                for (int j = 0; j < d; j++)
                    total[i, j] = sampleCov[i, j];
        for (int i = 0; i < d; i++)
        {
            double v = 0;
            for (int j = 0; j < p.Length; j++) v += p[j] * emVar[i, j];
            total[i, i] += v;
        }
        return total;
    }

    private static double[] Zero(int k) { var z = new double[k]; return z; }
    private static double[] Ones(int k) { var z = new double[k]; Array.Fill(z, 1.0); return z; }

    /// <summary>
    /// 在比例单纯形上枚举 χ²(p) ≤ 阈值的可行格点，并用洪水填充找互不连通区。
    /// k=2 用一维格点；k=3 用 (u,v) 三角重心格点。
    /// </summary>
    private static List<FeasibleRegion> EnumerateRegions(int n, int k,
        double[,] design, double[] y, double[,]? sampleCov, double[,] emVar,
        double[] lower, double[] upper, double threshold, int d)
    {
        var points = new List<(int I, int J, double U, double V, double[] P)>();
        if (k == 2)
        {
            for (int i = 0; i <= n; i++)
            {
                double u = (double)i / n;
                var p = new[] { 1 - u, u };
                if (WithinBox(p, lower, upper)) points.Add((i, 0, u, 0, p));
            }
        }
        else if (k == 3)
        {
            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n - i; j++)
            {
                double u = (double)i / n, v = (double)j / n;
                var p = new[] { u, v, 1 - u - v };
                if (WithinBox(p, lower, upper)) points.Add((i, j, u, v, p));
            }
        }
        else
        {
            // k>3：在重心格点抽样（低分辨率），可行域以候选点集合近似表达。
            return EnumerateHighDimension(k, design, y, sampleCov, emVar, lower, upper, threshold);
        }

        // 逐点 χ²：相关误差按总协方差传播。
        var feasible = new bool[points.Count];
        var chis = new double[points.Count];
        for (int idx = 0; idx < points.Count; idx++)
        {
            var (_, _, _, _, p) = points[idx];
            var resid = Residual(design, p, y);
            var cov = TotalAt(sampleCov, emVar, p, d);
            double chi = LA.QuadraticInverse(cov, resid);
            chis[idx] = chi;
            feasible[idx] = chi <= threshold;
        }

        // 洪水填充（三角网格六邻域 / 一维两邻域）。
        var byKey = new Dictionary<(int, int), int>();
        for (int idx = 0; idx < points.Count; idx++) byKey[(points[idx].I, points[idx].J)] = idx;
        var regionOf = new int[points.Count];
        Array.Fill(regionOf, -1);
        var regionsRaw = new List<List<int>>();
        foreach (var start in Enumerable.Range(0, points.Count))
        {
            if (!feasible[start] || regionOf[start] != -1) continue;
            int rid = regionsRaw.Count;
            var stack = new Stack<int>();
            stack.Push(start);
            regionOf[start] = rid;
            var members = new List<int>();
            var deltas = k == 2
                ? new[] { (1, 0), (-1, 0) }
                : new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, -1), (-1, 1) };
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                members.Add(cur);
                var (ci, cj, _, _, _) = points[cur];
                foreach (var (di, dj) in deltas)
                {
                    if (byKey.TryGetValue((ci + di, cj + dj), out var nb)
                        && feasible[nb] && regionOf[nb] == -1)
                    {
                        regionOf[nb] = rid;
                        stack.Push(nb);
                    }
                }
            }
            regionsRaw.Add(members);
        }

        var output = new List<FeasibleRegion>();
        for (int r = 0; r < regionsRaw.Count; r++)
        {
            var cells = regionsRaw[r].Select(idx => (points[idx].U, points[idx].V)).ToList();
            int bestIdx = regionsRaw[r].OrderBy(idx => chis[idx]).First();
            var bestP = points[bestIdx].P;
            var centroid = Centroid(regionsRaw[r].Select(idx => points[idx].P));
            var candidates = BuildCandidates(regionsRaw[r], points, chis, design, y, r);
            output.Add(new FeasibleRegion(
                $"region-{r + 1}",
                cells,
                centroid,
                bestP,
                chis[bestIdx],
                candidates));
        }
        return output;
    }

    private static List<FeasibleCandidate> BuildCandidates(List<int> memberIdxs,
        List<(int I, int J, double U, double V, double[] P)> points, double[] chis,
        double[,] design, double[] y, int regionIndex)
    {
        // 每个连通区保留：最优格点 + 各顶点附近极值 + 质心，全部为合法候选。
        var chosen = new List<int>();
        int best = memberIdxs.OrderBy(idx => chis[idx]).First();
        chosen.Add(best);
        foreach (var vertex in new[] { (0, 0), (points[best].P.Length == 2 ? points.Max(t => t.I) : points.Max(t => t.I), 0) })
        {
            var near = memberIdxs
                .OrderBy(idx => Math.Abs(points[idx].U - (vertex.Item1 == 0 ? 0 : 1)) + Math.Abs(points[idx].V))
                .FirstOrDefault(-1);
            if (near >= 0 && !chosen.Contains(near)) chosen.Add(near);
        }
        int centroidTarget = memberIdxs
            .OrderBy(idx =>
            {
                var c = Centroid(memberIdxs.Select(m => points[m].P));
                return points[idx].P.Zip(c, (a, b) => (a - b) * (a - b)).Sum();
            }).First();
        if (!chosen.Contains(centroidTarget)) chosen.Add(centroidTarget);

        var list = new List<FeasibleCandidate>();
        int n = 0;
        foreach (var idx in chosen)
        {
            n++;
            list.Add(new FeasibleCandidate(
                $"cand-r{regionIndex + 1}-{n}",
                $"region-{regionIndex + 1}",
                points[idx].P,
                chis[idx],
                Residual(design, points[idx].P, y)));
        }
        return list;
    }

    private static double[] Centroid(IEnumerable<double[]> ps)
    {
        var arr = ps.ToList();
        int k = arr[0].Length;
        var c = new double[k];
        foreach (var p in arr) for (int j = 0; j < k; j++) c[j] += p[j];
        for (int j = 0; j < k; j++) c[j] /= arr.Count;
        return c;
    }

    private static bool WithinBox(double[] p, double[] lo, double[] up)
    {
        for (int j = 0; j < p.Length; j++)
            if (p[j] < lo[j] - 1e-9 || p[j] > up[j] + 1e-9) return false;
        return true;
    }

    private static List<FeasibleRegion> EnumerateHighDimension(int k,
        double[,] design, double[] y, double[,]? sampleCov, double[,] emVar,
        double[] lower, double[] upper, double threshold)
    {
        var d = design.GetLength(0);
        var candidates = new List<FeasibleCandidate>();
        // 拉丁式网格抽样：单体内均匀取点，量随 k 控制。
        var rng = new DeterministicSequence(1234567);
        int samples = 4000;
        for (int t = 0; t < samples; t++)
        {
            var p = RandomSimplex(k, rng);
            if (!WithinBox(p, lower, upper)) continue;
            var resid = Residual(design, p, y);
            var cov = TotalAt(sampleCov, emVar, p, d);
            double chi = LA.QuadraticInverse(cov, resid);
            if (chi <= threshold)
                candidates.Add(new FeasibleCandidate($"cand-hi-{t}", "region-1", p, chi, resid));
        }
        if (candidates.Count == 0) return new();
        var region = new FeasibleRegion("region-1",
            candidates.Take(400).Select(c => (c.Fractions[0], c.Fractions[1])).ToList(),
            Centroid(candidates.Select(c => c.Fractions)),
            candidates.OrderBy(c => c.ChiSquare).First().Fractions,
            candidates.Min(c => c.ChiSquare),
            candidates.OrderBy(c => c.ChiSquare).Take(12).ToList());
        return new List<FeasibleRegion> { region };
    }

    private static double[] RandomSimplex(int k, DeterministicSequence rng)
    {
        var e = new double[k + 1];
        for (int i = 0; i <= k; i++) e[i] = -Math.Log(1 - rng.Next());
        double s = e.Sum();
        return e.Take(k).Select(x => x / s).ToArray();
    }

    /// <summary>样品是否落在端元凸包之外（无误差的几何判定，用于无可行点时归因）。</summary>
    private static bool OutsideGeometricEnvelope(double[,] design, double[] y, int k, int d,
        double[] lower, double[] upper)
    {
        var best = LA.ConstrainedFit(design, y, lower, upper);
        if (best is null) return true;
        var resid = Residual(design, best, y);
        double norm2 = resid.Sum(v => v * v);
        double scale = 0;
        for (int i = 0; i < d; i++)
            for (int j = 0; j < k; j++)
                scale += design[i, j] * design[i, j];
        scale = Math.Sqrt(scale / (d * k));
        return norm2 > (0.05 * scale) * (0.05 * scale);
    }

    private sealed class DeterministicSequence
    {
        private ulong _state;
        public DeterministicSequence(ulong seed) => _state = seed == 0 ? 1 : seed;
        public double Next()
        {
            _state ^= _state << 13; _state ^= _state >> 7; _state ^= _state << 17;
            return (_state >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}

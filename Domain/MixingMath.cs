using System;
using System.Collections.Generic;
using System.Linq;

namespace IsoMix.Domain;

/// <summary>
/// 稳定同位素混合反演。
/// 规则：端元近共线 -> 明确报告可识别性不足；比例始终满足和为一、非负（直接在单纯形上求解，
/// 绝不“截断负值后归一化”）；相关误差按完整协方差传播；保留全部合法候选（多个可行区）。
/// </summary>
public static partial class MixingMath
{
    public const string IdNearCollinear = "IDENTIFIABILITY.NEAR_COLLINEAR";
    public const string IdUnderdetermined = "IDENTIFIABILITY.UNDERDETERMINED";
    public const string IdSampleOutsideEnvelope = "GEOMETRY.SAMPLE_OUTSIDE_ENVELOPE";
    public const string IdCorrelatedErrors = "COVARIANCE.CORRELATED_ERRORS_PROPAGATED";
    public const string IdMultipleRegions = "FEASIBILITY.MULTIPLE_REGIONS";
    public const string IdNoRegion = "FEASIBILITY.NO_FEASIBLE_REGION";
    public const string IdPriorInfeasible = "PRIOR.INFEASIBLE";
    public const string IdConstraintExact = "CONSTRAINT.SUM_ONE_NONNEGATIVE_EXACT";
    public const string IdMissingAxes = "DIMENSIONS.MISSING_AXES_REDUCED";

    internal const int GridN = 120;

    public static MixingResult Solve(MixingInput input)
    {
        var diag = new List<Diagnostic>();
        int d = input.Observation.Length;
        int k = input.EndmemberIds.Count;
        var G = input.EndmemberMatrix;

        var result = new MixingResult
        {
            Axes = input.Axes.ToList(),
            EndmemberIds = input.EndmemberIds.ToList(),
            EndmemberNames = input.EndmemberNames.ToList(),
            Observation = (double[])input.Observation.Clone(),
            EndmemberMatrix = (double[,])G.Clone(),
            ChiSquareThreshold = MatrixOps.ChiSquareQuantile95(d)
        };

        double[,] sigma = EnsureCovariance(input, d, diag);
        double[,] invSigma = MatrixOps.InvertSymmetric(sigma);

        Identifiability(G, sigma, input.IdentifiabilityThreshold, result, diag);

        // 点估计：非负、和为一的约束 GLS（枚举活动集），不做截断归一化。
        var (point, clipped) = ConstrainedGlS(G, input.Observation, invSigma, k);
        result.PointEstimate = point;
        result.PointEstimateClipped = clipped;

        // 包络判定（在观测空间求到凸包的最小马氏距离）。
        result.EnvelopeMahalanobis = MinMahalanobisInHull(G, input.Observation, invSigma, k);
        result.SampleInsideEnvelope = result.EnvelopeMahalanobis <= result.ChiSquareThreshold + 1e-7;
        if (!result.SampleInsideEnvelope)
            diag.Add(new Diagnostic
            {
                Code = IdSampleOutsideEnvelope,
                Severity = Severity.Warning,
                Message = "样品观测位于端元凸包（含测量不确定度）之外：混合解释在给定置信水平下不可行。",
                AffectedObjectIds = input.EndmemberIds.ToList()
            });

        // 先验可行性：单纯形 ∩ 先验盒；为空即不可行先验（不伪造）。
        var priLo = input.PriorLo ?? Enumerable.Repeat(0.0, k).ToArray();
        var priHi = input.PriorHi ?? Enumerable.Repeat(1.0, k).ToArray();
        bool priorNonEmpty = SimplexBoxNonEmpty(priLo, priHi);
        if (!priorNonEmpty)
        {
            result.PriorInfeasible = true;
            result.Feasible = false;
            diag.Add(new Diagnostic
            {
                Code = IdPriorInfeasible,
                Severity = Severity.Error,
                Message = "先验区间与“和为一、非负”的混合单纯形交集为空：该先验不可行，未给出伪造比例。"
            });
        }

        // 近共线/欠定时仍保留几何结果，但不主张唯一比例。
        bool mapRegion = k == 3 && d >= 2 && priorNonEmpty;
        if (mapRegion)
            MapFeasibleRegion(input, sigma, invSigma, priLo, priHi, result, diag);
        else if (priorNonEmpty)
        {
            result.Feasible = result.SampleInsideEnvelope;
            if (!result.Feasible)
                diag.Add(new Diagnostic { Code = IdNoRegion, Severity = Severity.Warning, Message = "单纯形内不存在落在置信椭球内的比例候选。" });
        }

        // 代表点残差。
        var rep = result.Components.OrderBy(c => c.Representative?.MahalanobisSquared ?? double.MaxValue).FirstOrDefault()?.Representative;
        if (rep != null)
        {
            double[] pred = MatrixOps.MulVec(G, rep.Fractions);
            double[] r = new double[d];
            double sq = 0;
            for (int i = 0; i < d; i++) { r[i] = input.Observation[i] - pred[i]; sq += r[i] * r[i]; }
            result.Residual = r;
            result.RmsResidual = Math.Sqrt(sq / d);
            result.BestMahalanobisSquared = rep.MahalanobisSquared;
        }

        diag.Add(new Diagnostic
        {
            Code = IdConstraintExact,
            Severity = Severity.Info,
            Message = "所有候选比例严格满足 Σf=1 且 f≥0；直接在单纯形上求解，未使用截断后归一化。"
        });

        result.Diagnostics = diag;
        return result;
    }

    private static double[,] EnsureCovariance(MixingInput input, int d, List<Diagnostic> diag)
    {
        double[,] sigma;
        if (input.MeasurementCovariance is { } given)
        {
            sigma = given;
            bool correlated = false;
            for (int i = 0; i < d; i++)
                for (int j = i + 1; j < d; j++)
                    if (Math.Abs(sigma[i, j]) > 1e-15) correlated = true;
            if (correlated)
                diag.Add(new Diagnostic
                {
                    Code = IdCorrelatedErrors,
                    Severity = Severity.Info,
                    Message = "检测到非对角协方差：误差相关，已按完整协方差矩阵进行马氏距离与 GLS 传播。"
                });
        }
        else
        {
            sigma = MatrixOps.Identity(d);
        }
        // 数值上确保对称正定。
        var s = (double[,])sigma.Clone();
        for (int i = 0; i < d; i++) s[i, i] += 1e-10;
        return s;
    }

    private static void Identifiability(double[,] G, double[,] sigma, double threshold, MixingResult result, List<Diagnostic> diag)
    {
        int d = G.GetLength(0), k = G.GetLength(1);
        // 白化使阈值与量纲无关：先按测量协方差白化端元坐标。
        var invHalf = MatrixOps.InvertSymmetric(MatrixOps.Cholesky(sigma));
        double[,] w = MatrixOps.Mul(invHalf, G);
        // 将 k 个端元点按“质心”在 d 维同位素空间中心化（每行按端元取均值），
        // 再做 PCA：端元点若近共线，则最细的空间方向方差趋近于 0。
        var wc = (double[,])w.Clone();
        for (int r = 0; r < d; r++)
        {
            double rowMean = 0;
            for (int c = 0; c < k; c++) rowMean += w[r, c];
            rowMean /= k;
            for (int c = 0; c < k; c++) wc[r, c] = w[r, c] - rowMean;
        }
        var sv = MatrixOps.SingularValues(wc);
        double sMax = sv.Length == 0 ? 0 : sv[0];
        // 有信息方向数 = min(d, k-1)；最细的有信息方向决定近共线。
        int rankDim = Math.Min(d, k - 1);
        double sMin = rankDim >= 1 ? sv[rankDim - 1] : 0;
        double cond = sMin > 1e-15 ? sMax / sMin : double.PositiveInfinity;
        result.SingularValues = sv;
        result.ConditionNumber = cond;

        bool underdetermined = d < k - 1;
        bool nearCollinear = sMin <= threshold * Math.Max(1.0, sMax);
        if (underdetermined || nearCollinear)
        {
            result.Identifiable = false;
            diag.Add(new Diagnostic
            {
                Code = underdetermined ? IdUnderdetermined : IdNearCollinear,
                Severity = Severity.Warning,
                Message = underdetermined
                    ? "观测同位素维数不足，端元比例不可唯一识别：保留全部合法候选而非给出单点答案。"
                    : $"端元在同位素空间近共线（白化后最细方向奇异值 {sMin:E3}，条件数 {cond:E2}）：可识别性不足，应报告可行域而非唯一比例。",
                AffectedObjectIds = result.EndmemberIds.ToList()
            });
        }
    }

    private static (double[] point, bool clipped) ConstrainedGlS(double[,] G, double[] y, double[,] invSigma, int k)
    {
        // 在 f>=0, sum f =1 下最小化 (y-Gf)' Σ^-1 (y-Gf)。枚举活动边界求解 KKT。
        double[]? best = null; double bestQ = double.MaxValue;
        // 候选活动集：非负约束的子集（枚举规模对 k<=4 可接受）。
        var subsets = Enumerable.Range(0, k)
            .SelectMany(_ => new[] { false, true }, (idx, active) => (idx, active))
            .ToList();
        int combos = 1 << k;
        for (int mask = 0; mask < combos; mask++)
        {
            var zeroed = new List<int>();
            for (int j = 0; j < k; j++) if ((mask & (1 << j)) != 0) zeroed.Add(j);
            if (zeroed.Count == k) continue;
            var free = Enumerable.Range(0, k).Where(j => !zeroed.Contains(j)).ToArray();
            var f = EqualityConstrainedMin(G, y, invSigma, free, zeroed);
            if (f is null) continue;
            bool feasible = true;
            for (int j = 0; j < k; j++) if (f[j] < -1e-8) feasible = false;
            if (!feasible) continue;
            double[] r = MatrixOps.MulVec(G, f);
            for (int i = 0; i < r.Length; i++) r[i] = y[i] - r[i];
            double q = MatrixOps.Quadratic(invSigma, r);
            if (q < bestQ) { bestQ = q; best = f; }
        }
        bool clipped = best is not null && best.Any(v => v <= 1e-9);
        return (best ?? Uniform(k), clipped);
    }

    // 在指定自由变量（其余=0）且 sum f=1 等式约束下解加权最小二乘。
    private static double[]? EqualityConstrainedMin(double[,] G, double[] y, double[,] invSigma, int[] free, List<int> zeroed)
    {
        int d = G.GetLength(0), m = free.Length;
        var A = new double[d, m];
        for (int r = 0; r < d; r++) for (int c = 0; c < m; c++) A[r, c] = G[r, free[c]];
        var At = MatrixOps.Transpose(A);
        var H = MatrixOps.Mul(MatrixOps.Mul(At, invSigma), A); // m x m
        var rhs0 = MatrixOps.MulVec(MatrixOps.Mul(At, invSigma), y); // m
        // 约束 1'f=1，KKT
        int n = m;
        var K = new double[n + 1, n + 1];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) K[i, j] = H[i, j];
        var b = new double[n + 1];
        for (int i = 0; i < n; i++) { K[i, n] = 1; K[n, i] = 1; b[i] = rhs0[i]; }
        b[n] = 1;
        var sol = SolveSymmetric(K, b);
        if (sol is null) return null;
        var f = new double[G.GetLength(1)];
        for (int z = 0; z < zeroed.Count; z++) f[zeroed[z]] = 0;
        for (int c = 0; c < m; c++) f[free[c]] = sol[c];
        return f;
    }

    private static double[]? SolveSymmetric(double[,] K, double[] b)
    {
        int n = b.Length;
        var a = (double[,])K.Clone();
        var x = (double[])b.Clone();
        for (int p = 0; p < n; p++)
        {
            int piv = p;
            for (int r = p + 1; r < n; r++) if (Math.Abs(a[r, p]) > Math.Abs(a[piv, p])) piv = r;
            if (Math.Abs(a[piv, p]) < 1e-14) return null;
            for (int c = 0; c < n; c++) (a[p, c], a[piv, c]) = (a[piv, c], a[p, c]);
            (x[p], x[piv]) = (x[piv], x[p]);
            for (int r = 0; r < n; r++)
            {
                if (r == p) continue;
                double fac = a[r, p] / a[p, p];
                for (int c = p; c < n; c++) a[r, c] -= fac * a[p, c];
                x[r] -= fac * x[p];
            }
        }
        for (int i = 0; i < n; i++) x[i] /= a[i, i];
        return x;
    }

    private static double[] Uniform(int k) { var u = new double[k]; for (int i = 0; i < k; i++) u[i] = 1.0 / k; return u; }

    // 观测空间端元凸包内的最小马氏距离平方（在比例单纯形上）。
    private static double MinMahalanobisInHull(double[,] G, double[] y, double[,] invSigma, int k)
    {
        var (f, _) = ConstrainedGlS(G, y, invSigma, k);
        double[] pred = MatrixOps.MulVec(G, f);
        double[] r = new double[y.Length];
        for (int i = 0; i < r.Length; i++) r[i] = y[i] - pred[i];
        return MatrixOps.Quadratic(invSigma, r);
    }

    private static bool SimplexBoxNonEmpty(double[] lo, double[] hi)
    {
        int k = lo.Length;
        if (lo.Any(v => v < -1e-9)) return false;
        for (int i = 0; i < k; i++) if (hi[i] < lo[i] - 1e-9) return false;
        double sumLo = lo.Sum();
        double sumHi = hi.Sum();
        return sumLo <= 1.0 + 1e-9 && sumHi >= 1.0 - 1e-9;
    }

    internal static partial void MapFeasibleRegion(MixingInput input, double[,] sigma, double[,] invSigma, double[] priLo, double[] priHi, MixingResult result, List<Diagnostic> diag);
}

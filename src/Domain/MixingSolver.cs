namespace IsotopeMix.Domain;

/// <summary>
/// 混合比例可行域求解：
///   min χ²(x) 受 x>=0、sum x =1、（可选）先验边界约束；
/// 不做“截断后归一化”，所有可行点均满足单纯形约束。相关误差按协方差传播。
/// </summary>
public sealed class MixingSolver
{
    private const double GridStep = 0.02;
    private const double EigenFloor = 1e-10;

    private readonly double[,] _b;
    private readonly double[] _y;
    private readonly double[,] _sigmaInv;
    private readonly double _threshold;
    private readonly int _k;
    private readonly int _n;
    private readonly Dictionary<string, double[]>? _bounds;
    private readonly IReadOnlyList<string> _codes;

    public MixingSolver(MixingRequest req, double zScore)
    {
        _k = req.Sample.Length;
        _n = req.Endmembers.Count;
        _b = new double[_k, _n];
        for (var r = 0; r < _k; r++)
        for (var c = 0; c < _n; c++)
            _b[r, c] = req.Endmembers[c][r];
        _y = req.Sample;
        var sigma = new double[_k, _k];
        for (var i = 0; i < _k; i++)
        for (var j = 0; j < _k; j++)
            sigma[i, j] = req.Covariance[i][j];
        _sigmaInv = LinearAlgebra.SymmetricInverse(sigma, EigenFloor);
        _threshold = LinearAlgebra.ChiSquareUpperQuantile(_k, zScore);
        _bounds = req.PriorBounds;
        _codes = req.MemberCodes;
    }

    public double Chi2(double[] x)
    {
        var resid = new double[_k];
        for (var r = 0; r < _k; r++)
        {
            var pred = 0.0;
            for (var c = 0; c < _n; c++) pred += _b[r, c] * x[c];
            resid[r] = _y[r] - pred;
        }
        var chi = 0.0;
        for (var i = 0; i < _k; i++)
        for (var j = 0; j < _k; j++)
            chi += resid[i] * _sigmaInv[i, j] * resid[j];
        return chi;
    }

    public double[] Residuals(double[] x)
    {
        var resid = new double[_k];
        for (var r = 0; r < _k; r++)
        {
            var pred = 0.0;
            for (var c = 0; c < _n; c++) pred += _b[r, c] * x[c];
            resid[r] = _y[r] - pred;
        }
        return resid;
    }

    private bool InPriorBounds(double[] x)
    {
        if (_bounds is null) return true;
        for (var i = 0; i < _n; i++)
            if (_bounds.TryGetValue(_codes[i], out var ab))
                if (x[i] < ab[0] - 1e-10 || x[i] > ab[1] + 1e-10) return false;
        return true;
    }

    private bool PriorExcludesWholeSimplex()
    {
        if (_bounds is null) return false;
        var losum = 0.0;
        var hisum = 0.0;
        foreach (var code in _codes)
        {
            var ab = _bounds.TryGetValue(code, out var v) ? v : new[] { 0.0, 1.0 };
            if (ab[0] < 0 || ab[1] > 1 || ab[0] > ab[1]) return true;
            losum += ab[0];
            hisum += ab[1];
        }
        return losum > 1.0 + 1e-9 || hisum < 1.0 - 1e-9;
    }

    public List<double[]> GridPoints() => Compositions(_n, GridStep).Where(InPriorBounds).ToList();

    public static IEnumerable<double[]> Compositions(int n, double step)
    {
        var count = (int)Math.Round(1.0 / step);
        var x = new double[n];
        IEnumerable<double[]> Gen(int dim, int remaining)
        {
            if (dim == n - 1)
            {
                x[dim] = remaining * step;
                yield return (double[])x.Clone();
                yield break;
            }
            for (var v = 0; v <= remaining; v++)
            {
                x[dim] = v * step;
                foreach (var p in Gen(dim + 1, remaining - v)) yield return p;
            }
        }
        return Gen(0, count).ToList();
    }

    /// <summary>
    /// 沿单纯形内坐标方向做一维黄金分割（方向在保持和为一的超平面上，
    /// 并自动收束到非负/先验区间）——纯约束内搜索，绝不截断后重归一化。
    /// </summary>
    private double[] LineImprove(double[] x, double[] d, Func<double[], double> f)
    {
        var lo = 0.0;
        var hi = double.PositiveInfinity;
        for (var i = 0; i < _n; i++)
        {
            if (Math.Abs(d[i]) < 1e-15) continue;
            var t0 = -x[i] / d[i];
            if (d[i] < 0) lo = Math.Max(lo, t0);
            else hi = Math.Min(hi, t0);
            if (_bounds != null && _bounds.TryGetValue(_codes[i], out var ab))
            {
                var tlo = (ab[0] - x[i]) / d[i];
                var thi = (ab[1] - x[i]) / d[i];
                lo = Math.Max(lo, Math.Min(tlo, thi));
                hi = Math.Min(hi, Math.Max(tlo, thi));
            }
        }
        if (hi - lo < 1e-12 || double.IsInfinity(hi) == false && hi < lo) return x;
        hi = double.IsInfinity(hi) ? lo + 1.0 : hi;

        var a = lo; var b = hi;
        var gr = (Math.Sqrt(5) - 1) / 2;
        var c = b - gr * (b - a);
        var d1 = a + gr * (b - a);
        while (b - a > 1e-7)
        {
            if (f(Move(x, d, c)) < f(Move(x, d, d1))) b = d1;
            else a = c;
            c = b - gr * (b - a);
            d1 = a + gr * (b - a);
        }
        var t = (a + b) / 2;
        var best = Move(x, d, t);
        return f(best) < f(x) ? best : x;
    }

    private static double[] Move(double[] x, double[] d, double t)
    {
        // 方向 d 已在“和为一”超平面上（分量和为 0），移动后仍精确满足
        // 非负与和为一；这里不做任何截断或归一化。
        var z = new double[x.Length];
        for (var i = 0; i < x.Length; i++) z[i] = x[i] + t * d[i];
        return z;
    }

    /// <summary>从可行（或暂时违反先验）起点做单纯形坐标搜索，求最小 χ²。</summary>
    public double[] MinimizeChi2()
    {
        var grid = Compositions(_n, GridStep).Where(InPriorBounds).ToList();
        var best = grid.Count > 0
            ? grid.OrderBy(Chi2).First()
            : Enumerable.Range(0, _n).Select(i => i == 0 ? 1.0 : 0.0).ToArray();
        for (var iter = 0; iter < 40; iter++)
        {
            var moved = false;
            for (var i = 0; i < _n; i++)
            for (var j = i + 1; j < _n; j++)
            {
                var dir = new double[_n];
                dir[i] = 1; dir[j] = -1;
                var candidate = LineImprove(best, dir, Chi2);
                if (Chi2(candidate) < Chi2(best) - 1e-10) { best = candidate; moved = true; }
            }
            if (!moved) break;
        }
        return best;
    }

    public CandidateResult SolveCandidate(string candidateId, string modeId)
    {
        var result = new CandidateResult { CandidateId = candidateId, ModeId = modeId };
        if (PriorExcludesWholeSimplex())
        {
            result.Status = "prior_infeasible";
            result.Chi2Min = double.NaN;
            result.Threshold = _threshold;
            result.WithinEnvelope = false;
            return result;
        }

        var xStar = MinimizeChi2();
        var chiMin = Chi2(xStar);
        result.Chi2Min = chiMin;
        result.Threshold = _threshold;
        result.Residuals = Residuals(xStar);

        if (chiMin > _threshold)
        {
            result.Status = "infeasible";
            var hullDist = Math.Sqrt(chiMin) - Math.Sqrt(_threshold);
            result.Warnings.Add($"样品在 {1 - 0.05:F0}% 置信下超出端元包络（χ²={chiMin:F2} > {_threshold:F2}）。");
            result.WithinEnvelope = false;
            return result;
        }
        result.Status = "feasible";
        result.WithinEnvelope = true;

        var feasible = new List<double[]>();
        foreach (var g in GridPoints())
            if (InPriorBounds(g) && Chi2(g) <= _threshold) feasible.Add(g);

        // 以最小点为种子，把各比例的上下限沿边缘推到 χ² 门槛。
        var lower = Enumerable.Repeat(double.PositiveInfinity, _n).ToArray();
        var upper = Enumerable.Repeat(double.NegativeInfinity, _n).ToArray();
        foreach (var p in feasible)
            for (var i = 0; i < _n; i++)
            {
                lower[i] = Math.Min(lower[i], p[i]);
                upper[i] = Math.Max(upper[i], p[i]);
            }

        for (var i = 0; i < _n; i++)
        {
            var dirMax = BoundaryDirection(xStar, i, +1);
            var tMax = RayToBoundary(xStar, dirMax);
            if (tMax.HasValue) upper[i] = Math.Max(upper[i], (xStar[i] + tMax.Value * dirMax[i]));
            var dirMin = BoundaryDirection(xStar, i, -1);
            var tMin = RayToBoundary(xStar, dirMin);
            if (tMin.HasValue) lower[i] = Math.Min(lower[i], (xStar[i] + tMin.Value * dirMin[i]));
        }

        // 从每个网格可行点向坐标轴极值补充扫描，提升区间覆盖。
        foreach (var seed in feasible.Take(400))
        for (var i = 0; i < _n; i++)
        {
            var tm = RayToBoundary(seed, BoundaryDirection(seed, i, +1));
            if (tm.HasValue) upper[i] = Math.Max(upper[i], seed[i] + tm.Value * BoundaryDirection(seed, i, +1)[i]);
            var tn = RayToBoundary(seed, BoundaryDirection(seed, i, -1));
            if (tn.HasValue) lower[i] = Math.Min(lower[i], seed[i] + tn.Value * BoundaryDirection(seed, i, -1)[i]);
        }

        for (var i = 0; i < _n; i++)
        {
            if (double.IsInfinity(lower[i])) lower[i] = 0;
            if (double.IsNegativeInfinity(upper[i])) upper[i] = 0;
        }
        result.Lower = lower;
        result.Upper = upper;
        result.Centroid = feasible.Count > 0
            ? Enumerable.Range(0, _n).Select(i => feasible.Average(p => p[i])).ToArray()
            : xStar;
        result.FeasibleGrid = feasible.Take(1200).ToList();
        return result;
    }

    private double[] BoundaryDirection(double[] x, int index, int sign)
    {
        // 在单纯形切空间内沿“增大/减小 x_index”的归一化方向（与单位向量等分量平衡）。
        var dir = new double[_n];
        dir[index] = sign;
        // 从其余成员中选一个可行的对侧，保持和为一。
        var partner = -1;
        for (var j = 0; j < _n; j++)
            if (j != index && (sign < 0 ? x[j] > 1e-9 : true) && InPairRange(x, j, -sign)) { partner = j; break; }
        if (partner < 0)
        {
            for (var j = 0; j < _n; j++) if (j != index) { partner = j; break; }
        }
        if (partner >= 0) dir[partner] = -sign;
        var norm = Math.Sqrt(dir.Sum(v => v * v));
        for (var i = 0; i < _n; i++) dir[i] /= norm;
        return dir;
    }

    private bool InPairRange(double[] x, int j, int d) => true;

    private double? RayToBoundary(double[] x, double[] dir)
    {
        bool Inside(double[] z) => AllNonnegative(z) && InPriorBounds(z) && Chi2(z) <= _threshold;
        if (!Inside(ClampPoint(x))) return null;
        double lo = 0, hi = 1.0;
        while (Inside(Move(x, dir, hi))) hi *= 2;
        for (var it = 0; it < 40; it++)
        {
            var mid = (lo + hi) / 2;
            if (Inside(Move(x, dir, mid))) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    private static bool AllNonnegative(double[] z) => z.All(v => v >= -1e-9);
    private static double[] ClampPoint(double[] z) => z;
}

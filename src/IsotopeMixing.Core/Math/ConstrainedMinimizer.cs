namespace IsotopeMixing.Core.Math;

/// <summary>
/// 在单纯形 { p >= 0, sum p = 1 } 上最小化 f(p)。
/// 使用投影梯度: 梯度数值差分 + 回溯线搜索 + 精确单纯形投影(Duchi 等)。
/// 约束通过“投影”满足, 绝不是先截断负值再归一化的伪造做法。
/// </summary>
public sealed class ConstrainedMinimizer
{
    private readonly Func<double[], double> _f;
    private readonly int _n;
    private readonly Func<double[], double[]>? _gradient;
    private readonly double[]? _lower;
    private readonly double[]? _upper;

    public ConstrainedMinimizer(int n, Func<double[], double> f, Func<double[], double[]>? gradient = null,
        double[]? lower = null, double[]? upper = null)
    {
        _n = n; _f = f; _gradient = gradient;
        _lower = lower ?? Enumerable.Repeat(0.0, n).ToArray();
        _upper = upper ?? Enumerable.Repeat(1.0, n).ToArray();
    }

    public double[] Minimize(double[] start, int maxIter = 400, double tol = 1e-9, out double fval)
    {
        var p = ProjectBoxSimplex(start, _lower!, _upper!);
        fval = _f(p);
        double step = 1.0;
        for (int iter = 0; iter < maxIter; iter++)
        {
            var g = _gradient?.Invoke(p) ?? NumericGradient(p);
            // 投影梯度作为 KKT 停止度量
            var projected = ProjectBoxSimplex(p.SubstractScaled(g, 1e-5), _lower!, _upper!);
            double kkt = 0; for (int i = 0; i < _n; i++) kkt = System.Math.Max(kkt, System.Math.Abs(projected[i] - p[i]) / 1e-5);
            if (kkt < tol) break;

            // Barzilai-Borwein 步长 + 非单调可接受回退
            bool accepted = false;
            double t = System.Math.Max(step, 1e-4);
            for (int ls = 0; ls < 40; ls++)
            {
                var cand = ProjectBoxSimplex(p.SubstractScaled(g, t), _lower!, _upper!);
                double fc = _f(cand);
                double dirDot = LinearAlgebra.Dot(g, cand.Subtract(p));
                if (fc <= fval + 1e-4 * dirDot + 1e-12)
                {
                    var gp = _gradient?.Invoke(cand) ?? NumericGradient(cand);
                    double sy = LinearAlgebra.Dot(cand.Subtract(p), gp.Subtract(g));
                    double yy = gp.Subtract(g).SumSq();
                    if (yy > 1e-14) step = System.Math.Max(1e-4, System.Math.Min(1e4, cand.Subtract(p).SumSq() / sy));
                    p = cand; fval = fc; accepted = true; break;
                }
                t *= 0.5;
            }
            if (!accepted) break;
        }
        return p;
    }

    private double[] NumericGradient(double[] p)
    {
        var g = new double[_n];
        double h = 1e-6;
        for (int i = 0; i < _n; i++)
        {
            var pp = (double[])p.Clone(); pp[i] += h;
            var pm = (double[])p.Clone(); pm[i] -= h;
            g[i] = (_f(pp) - _f(pm)) / (2 * h);
        }
        return g;
    }

    /// <summary>
    /// 投影到 { lower &lt;= x &lt;= upper, sum x = 1 }。
    /// 盒子+和约束: 对单纯形投影做二分求拉格朗日乘子 (Held/Kerer/Pisinger 风格精确投影)。
    /// </summary>
    public static double[] ProjectBoxSimplex(double[] v, double[] lower, double[] upper)
    {
        int n = v.Length;
        double lo = v.Min() - 2, hi = v.Max() + 2;
        double sumLower = lower.Sum();
        // sum_i clamp(v_i - tau, l_i, u_i) = 1
        for (int it = 0; it < 100; it++)
        {
            double tau = (lo + hi) / 2, s = 0;
            for (int i = 0; i < n; i++) s += System.Math.Min(upper[i], System.Math.Max(lower[i], v[i] - tau));
            if (s > 1) lo = tau; else hi = tau;
            if (System.Math.Abs(s - 1) < 1e-11 && it > 30) break;
        }
        var x = new double[n];
        double t = (lo + hi) / 2;
        for (int i = 0; i < n; i++) x[i] = System.Math.Min(upper[i], System.Math.Max(lower[i], v[i] - t));
        // 数值修正: 保持精确和为 1 且不越界
        double err = 1 - x.Sum();
        if (System.Math.Abs(err) > 1e-12)
        {
            for (int i = 0; i < n && System.Math.Abs(err) > 1e-13; i++)
            {
                double add = err > 0 ? System.Math.Min(err, upper[i] - x[i]) : System.Math.Max(err, lower[i] - x[i]);
                x[i] += add; err -= add;
            }
        }
        return x;
    }
}

internal static class VecExtensions
{
    public static double[] SubstractScaled(this double[] a, double[] g, double s)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i] - s * g[i];
        return r;
    }
    public static double[] Subtract(this double[] a, double[] b)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = a[i] - b[i];
        return r;
    }
    public static double SumSq(this double[] a) { double s = 0; foreach (var x in a) s += x * x; return s; }
}

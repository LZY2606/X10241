namespace IsotopeMix.Domain;

/// <summary>
/// 带单纯形约束（比例和为一、非负）与可选箱型先验界的凸二次规划。
/// 关键不变量：解始终投影到真实约束多面体上，绝不使用“截断到[0,1]后除以总和”的伪造归一化。
/// 投影子问题 min 1/2||x-y||² s.t. sum x=1, l≤x≤u 用二分搜索拉格朗日乘子精确求解。
/// </summary>
public sealed class QuadraticProgram
{
    private readonly double[][] _h;
    private readonly double[] _f;
    private readonly double[] _lower;
    private readonly double[] _upper;
    private readonly int _n;

    public QuadraticProgram(double[][] h, double[] f, double[]? lower = null, double[]? upper = null)
    {
        _h = h;
        _f = f;
        _n = h.Length;
        _lower = lower ?? Enumerable.Repeat(0.0, _n).ToArray();
        _upper = upper ?? Enumerable.Repeat(1.0, _n).ToArray();
    }

    public bool IsFeasible()
    {
        for (int i = 0; i < _n; i++)
        {
            if (_lower[i] > _upper[i] + 1e-12) return false;
            if (_lower[i] < -1e-12 || _upper[i] > 1 + 1e-12) return false;
        }
        double loSum = _lower.Sum(), upSum = _upper.Sum();
        return loSum <= 1 + 1e-9 && upSum >= 1 - 1e-9;
    }

    /// <summary>均匀可行点，用于冷启动与可行性检验。</summary>
    public double[] FeasiblePoint()
    {
        var x = (double[])_lower.Clone();
        double remaining = 1 - x.Sum();
        var order = Enumerable.Range(0, _n).OrderByDescending(i => _upper[i]).ToArray();
        foreach (int i in order)
        {
            double add = Math.Min(remaining, _upper[i] - x[i]);
            if (add > 0) { x[i] += add; remaining -= add; }
            if (remaining <= 1e-12) break;
        }
        return x;
    }

    /// <summary>FISTA 投影梯度。返回 (最优点, 目标值, KKT 容差)。</summary>
    public (double[] x, double objective, double kktResidual) Solve(int maxIterations = 20000)
    {
        double[] x = FeasiblePoint(), y = (double[])x.Clone();
        double step = EstimateStep();
        double t = 1, prevObj = double.PositiveInfinity;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            double[] grad = LinearAlgebra.Multiply(_h, y);
            for (int i = 0; i < _n; i++) grad[i] += _f[i];

            double[] candidate = ProjectOntoSimplexBox(y, step, grad);
            double obj = Objective(candidate);

            // 回溯线搜索保证充分下降（FISTA 偶尔外推过头时）
            int backtracks = 0;
            double[] gy = LinearAlgebra.Multiply(_h, y);
            for (int i = 0; i < _n; i++) gy[i] += _f[i];
            while (obj > Objective(y) + Dot(gy, Diff(candidate, y)) +
                            DiffNormSq(candidate, y) / (2 * step) + 1e-10 && backtracks < 30)
            {
                step *= 0.5;
                candidate = ProjectOntoSimplexBox(y, step, grad);
                obj = Objective(candidate);
                backtracks++;
            }
            if (backtracks == 30) step *= 0.25;
            else if (backtracks == 0 && iter > 5) step = Math.Min(step * 1.05, EstimateStep());

            double tNew = (1 + Math.Sqrt(1 + 4 * t * t)) / 2;
            double[] nextX = candidate;
            double[] newY = new double[_n];
            double momentum = (t - 1) / tNew;
            for (int i = 0; i < _n; i++)
                newY[i] = Math.Clamp(nextX[i] + momentum * (nextX[i] - x[i]), 0, 1);
            newY = ProjectOntoSimplexBox(newY, 0, new double[_n]);
            x = nextX;
            y = newY;
            t = tNew;

            if (iter % 64 == 0 && Math.Abs(prevObj - obj) < 1e-13 * Math.Max(1, Math.Abs(obj)))
            {
                double kkt = KktResidual(x);
                if (kkt < 1e-8) return (x, obj, kkt);
            }
            prevObj = obj;
        }
        return (x, Objective(x), KktResidual(x));
    }

    private double EstimateStep()
    {
        // Gershgorin 谱半径上界 -> 1/L
        double l = 0;
        for (int i = 0; i < _n; i++)
        {
            double row = Math.Abs(_h[i][i]);
            for (int j = 0; j < _n; j++) if (i != j) row += Math.Abs(_h[i][j]);
            l = Math.Max(l, row);
        }
        return l <= 0 ? 1 : 1 / l;
    }

    private double[] ProjectOntoSimplexBox(double[] y, double step, double[] grad)
    {
        var z = new double[_n];
        for (int i = 0; i < _n; i++) z[i] = y[i] - step * grad[i];
        return Project(z, _lower, _upper);
    }

    /// <summary>
    /// 投影到 { x | sum x = 1, l ≤ x ≤ u }。
    /// 对活动集二分求 τ；τ 取上下界中值区间，精确满足等式约束。
    /// </summary>
    public static double[] Project(double[] z, double[] l, double[] u)
    {
        int n = z.Length;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            lo = Math.Min(lo, z[i] - u[i]);
            hi = Math.Max(hi, z[i] - l[i]);
        }
        lo -= 1; hi += 1;

        double[] x = new double[n];
        for (int iter = 0; iter < 80; iter++)
        {
            double tau = (lo + hi) / 2;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                x[i] = Math.Clamp(z[i] - tau, l[i], u[i]);
                sum += x[i];
            }
            if (Math.Abs(sum - 1) < 1e-12) break;
            if (sum > 1) lo = tau; else hi = tau;
        }
        return x;
    }

    private double Objective(double[] x)
    {
        var hx = LinearAlgebra.Multiply(_h, x);
        return 0.5 * Dot(x, hx) + Dot(_f, x);
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static double[] Diff(double[] a, double[] b)
    {
        var d = new double[a.Length];
        for (int i = 0; i < a.Length; i++) d[i] = a[i] - b[i];
        return d;
    }

    private static double DiffNormSq(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]) * (a[i] - b[i]);
        return s;
    }

    private double KktResidual(double[] x)
    {
        double[] grad = LinearAlgebra.Multiply(_h, x);
        for (int i = 0; i < _n; i++) grad[i] += _f[i];

        // 等和约束的乘子 μ 由内部分量梯度平均估计；边界分量检查符号。
        double mu = 0; int innerCount = 0;
        for (int i = 0; i < _n; i++)
        {
            bool inner = x[i] > _lower[i] + 1e-8 && x[i] < _upper[i] - 1e-8;
            if (inner) { mu += grad[i]; innerCount++; }
        }
        if (innerCount > 0) mu /= innerCount;

        double max = 0;
        for (int i = 0; i < _n; i++)
        {
            double r;
            if (x[i] <= _lower[i] + 1e-8) r = Math.Max(0, grad[i] - mu);
            else if (x[i] >= _upper[i] - 1e-8) r = Math.Max(0, mu - grad[i]);
            else r = Math.Abs(grad[i] - (innerCount > 0 ? mu : grad[i]));
            max = Math.Max(max, Math.Abs(r));
        }
        double eq = Math.Abs(x.Sum() - 1);
        return Math.Max(max, eq);
    }
}

namespace IsotopeMix.Core.LinearAlgebra;

/// <summary>
/// 仅依赖 .NET 的小型线性代数（维度 ≤3 的同位素混合问题）。
/// 所有数值路径都显式处理正定/奇异情况。
/// </summary>
public static class LA
{
    public static double[] SolvePositiveDefinite(double[,] a, double[] b)
    {
        var l = Cholesky(a);
        return SolveCholesky(l, b);
    }

    /// <summary>Cholesky 分解 a = L L^T。非正定抛出。</summary>
    public static double[,] Cholesky(double[,] a)
    {
        int n = a.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = 0;
                for (int k = 0; k < j; k++) sum += l[i, k] * l[j, k];
                if (i == j)
                {
                    double diag = a[i, i] - sum;
                    if (diiagSafe(diag)) throw new InvalidOperationException("矩阵非正定，无法 Cholesky 分解（请检查协方差）");
                    l[i, j] = Math.Sqrt(diag);
                }
                else l[i, j] = (a[i, j] - sum) / l[j, j];
            }
        }
        return l;
    }

    private static bool diiagSafe(double diag) => diag <= 1e-14;

    public static double[] SolveCholesky(double[,] l, double[] b)
    {
        int n = l.GetLength(0);
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            for (int k = 0; k < i; k++) sum += l[i, k] * y[k];
            y[i] = (b[i] - sum) / l[i, i];
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0;
            for (int k = i + 1; k < n; k++) sum += l[k, i] * x[k];
            x[i] = (y[i] - sum) / l[i, i];
        }
        return x;
    }

    /// <summary>x^T a^{-1} x，使用 Cholesky；非正定回退到对称特征分解。</summary>
    public static double QuadraticInverse(double[,] a, double[] x)
    {
        double[] z;
        try
        {
            var l = Cholesky(a);
            z = SolveCholesky(l, x);
        }
        catch (InvalidOperationException)
        {
            var inv = PseudoInverseSymmetric(a);
            z = Multiply(inv, x);
        }
        double q = 0;
        for (int i = 0; i < x.Length; i++) q += x[i] * z[i];
        return q;
    }

    public static double[] Multiply(double[,] a, double[] x)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int j = 0; j < m; j++) s += a[i, j] * x[j];
            r[i] = s;
        }
        return r;
    }

    /// <summary>2x2/3x3 对称矩阵 Jacobi 特征分解（稳健回退用）。</summary>
    public static (double[] values, double[,] vectors) EigenSymmetric(double[,] a)
    {
        int n = a.GetLength(0);
        var v = Identity(n);
        var work = Copy(a);
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++) off += work[p, q] * work[p, q];
            if (off < 1e-24) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(work[p, q]) < 1e-15) continue;
                    double tau = (work[q, q] - work[p, p]) / (2 * work[p, q]);
                    double t = (tau >= 0 ? 1 : -1) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1 / Math.Sqrt(1 + t * t), s = t * c;
                    Rotate(work, v, n, p, q, c, s);
                }
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = work[i, i];
        return (vals, v);
    }

    private static void Rotate(double[,] a, double[,] v, int n, int p, int q, double c, double s)
    {
        for (int k = 0; k < n; k++)
        {
            double akp = a[k, p], akq = a[k, q];
            a[k, p] = c * akp - s * akq;
            a[k, q] = s * akp + c * akq;
        }
        for (int k = 0; k < n; k++)
        {
            double apk = a[p, k], aqk = a[q, k];
            a[p, k] = c * apk - s * aqk;
            a[q, k] = s * apk + c * aqk;
        }
        for (int k = 0; k < n; k++)
        {
            double vkp = v[k, p], vkq = v[k, q];
            v[k, p] = c * vkp - s * vkq;
            v[k, q] = s * vkp + c * vkq;
        }
    }

    public static double[,] PseudoInverseSymmetric(double[,] a)
    {
        int n = a.GetLength(0);
        var (vals, vecs) = EigenSymmetric(a);
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(vals[i]) < 1e-12) continue;
            double invLambda = 1.0 / vals[i];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    inv[r, c] += vecs[r, i] * invLambda * vecs[c, i];
        }
        return inv;
    }

    /// <summary>
    /// 任意矩阵的奇异值（通过 A^T A 的 Jacobi 特征分解）。
    /// 用于端元近共线（最小奇异值塌缩）诊断。
    /// </summary>
    public static double[] SingularValues(double[,] a)
    {
        int rows = a.GetLength(0), cols = a.GetLength(1);
        var ata = new double[cols, cols];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
            {
                double s = 0;
                for (int r = 0; r < rows; r++) s += a[r, i] * a[r, j];
                ata[i, j] = s;
            }
        var (vals, _) = EigenSymmetric(ata);
        for (int i = 0; i < vals.Length; i++) vals[i] = Math.Sqrt(Math.Max(0, vals[i]));
        Array.Sort(vals);
        Array.Reverse(vals);
        return vals;
    }

    /// <summary>
    /// 投影到概率单纯形 {x≥0, sum x=1}（Duchi 等的精确投影）。
    /// 这不是“先截断负数再归一化”的伪造做法；它是到单纯形的真实欧氏投影，
    /// 若投影本身落在顶点/棱上，那是约束几何决定的合法结果。
    /// </summary>
    public static double[] ProjectSimplex(double[] v)
    {
        int n = v.Length;
        var u = (double[])v.Clone();
        Array.Sort(u);
        Array.Reverse(u);
        double cssv = 0, rho = 0;
        for (int i = 0; i < n; i++)
        {
            cssv += u[i];
            if (u[i] - (cssv - 1) / (i + 1) > 0) rho = i + 1;
        }
        double theta = 0;
        cssv = 0;
        for (int i = 0; i < rho; i++) cssv += u[i];
        theta = (cssv - 1) / rho;
        var w = new double[n];
        for (int i = 0; i < n; i++) w[i] = Math.Max(0, v[i] - theta);
        return w;
    }

    /// <summary>
    /// 带盒形先验的单纯形约束最小二乘（投影梯度）。
    /// 返回的比例严格满足非负、和为 1 且在盒形先验内；
    /// 若盒形先验本身与单纯形不相交，返回 null（不可行先验）。
    /// </summary>
    public static double[]? ConstrainedFit(double[,] design, double[] y,
        double[] lower, double[] upper)
    {
        int k = design.GetLength(1);
        if (!PriorIntersectsSimplex(lower, upper)) return null;

        var x = ProjectBoxSimplex(Center(lower, upper), lower, upper);
        double step = 1.0 / Math.Max(1, Lipschitz(design));
        for (int it = 0; it < 400; it++)
        {
            var residual = Subtract(Multiply(design, x), y);
            var grad = Scale(TransposeMultiply(design, residual), 2.0);
            var trial = ProjectBoxSimplex(Subtract(x, Scale(grad, step)), lower, upper);
            double shift = 0;
            for (int i = 0; i < k; i++) shift = Math.Max(shift, Math.Abs(trial[i] - x[i]));
            x = trial;
            if (shift < 1e-10) break;
        }
        return OnSimplex(x) ? x : ProjectBoxSimplex(x, lower, upper);
    }

    public static bool PriorIntersectsSimplex(double[] lower, double[] upper)
    {
        double loSum = 0, upSum = 0;
        for (int i = 0; i < lower.Length; i++)
        {
            if (lower[i] < -1e-12 || upper[i] > 1 + 1e-12 || upper[i] < lower[i] - 1e-12) return false;
            loSum += Math.Clamp(lower[i], 0, 1);
            upSum += Math.Clamp(upper[i], 0, 1);
        }
        return loSum <= 1 + 1e-9 && upSum >= 1 - 1e-9;
    }

    private static double[] Center(double[] lo, double[] up)
    {
        var c = new double[lo.Length];
        for (int i = 0; i < lo.Length; i++) c[i] = (lo[i] + up[i]) / 2;
        double s = c.Sum();
        if (s <= 0) return Uniform(lo.Length);
        for (int i = 0; i < c.Length; i++) c[i] /= s;
        return c;
    }

    private static double[] Uniform(int n)
    {
        var u = new double[n];
        Array.Fill(u, 1.0 / n);
        return u;
    }

    /// <summary>
    /// 精确投影到 { l_i ≤ x_i ≤ u_i, Σx_i = 1 }：
    /// 用一维水位参数 t 求根，x_i(t)=clamp(v_i−t, l_i, u_i) 使和为 1。
    /// 这是到该凸多面体的真实欧氏投影，不涉及截断后再归一化。
    /// 调用前必须用 PriorIntersectsSimplex 确认可行。
    /// </summary>
    private static double[] ProjectBoxSimplex(double[] v, double[] lo, double[] up)
    {
        double loT = -1e6, upT = 1e6;
        double SumAt(double t)
        {
            double z = 0;
            for (int i = 0; i < v.Length; i++) z += Math.Clamp(v[i] - t, lo[i], up[i]);
            return z;
        }
        double flo = SumAt(loT) - 1, fhi = SumAt(upT) - 1;
        if (flo < 0)
        {
            var forced = new double[v.Length];
            for (int i = 0; i < v.Length; i++) forced[i] = Math.Clamp(v[i] - loT, lo[i], up[i]);
            return forced;
        }
        for (int it = 0; it < 80; it++)
        {
            double mid = (loT + upT) / 2;
            double f = SumAt(mid) - 1;
            if (Math.Abs(f) < 1e-13) { loT = upT = mid; break; }
            if (f > 0) loT = mid; else upT = mid;
        }
        double tstar = (loT + upT) / 2;
        var x = new double[v.Length];
        for (int i = 0; i < v.Length; i++) x[i] = Math.Clamp(v[i] - tstar, lo[i], up[i]);
        return x;
    }

    private static double Lipschitz(double[,] a)
    {
        var sv = SingularValues(a);
        double g = sv.Length > 0 ? sv[0] : 1;
        return g * g + 1e-9;
    }

    private static double[] TransposeMultiply(double[,] a, double[] x)
    {
        int rows = a.GetLength(0), cols = a.GetLength(1);
        var r = new double[cols];
        for (int j = 0; j < cols; j++)
        {
            double s = 0;
            for (int i = 0; i < rows; i++) s += a[i, j] * x[i];
            r[j] = s;
        }
        return r;
    }

    private static double[] Subtract(double[] a, double[] b)
        => a.Zip(b, (x, y) => x - y).ToArray();

    private static double[] Scale(double[] a, double f)
        => a.Select(x => x * f).ToArray();

    private static double Norm(double[] a) => Math.Sqrt(a.Sum(x => x * x));

    public static bool OnSimplex(double[] x)
        => Math.Abs(x.Sum() - 1) < 1e-7 && x.All(v => v >= -1e-9);

    public static double[,] Identity(int n)
    {
        var i = new double[n, n];
        for (int k = 0; k < n; k++) i[k, k] = 1;
        return i;
    }

    public static double[,] Copy(double[,] a)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var c = new double[n, m];
        Array.Copy(a, c, a.Length);
        return c;
    }
}

namespace IsotopeMixing.Core.Math;

/// <summary>最小依赖线性代数: Cholesky、对称特征值(Jacobi)、矩阵工具。</summary>
public static class LinearAlgebra
{
    public static double[][] Identity(int n)
    {
        var r = new double[n][];
        for (int i = 0; i < n; i++) { r[i] = new double[n]; r[i][i] = 1; }
        return r;
    }

    public static double[][] Transpose(double[][] a)
    {
        int m = a.Length, n = a[0].Length;
        var r = Rect(n, m);
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                r[j][i] = a[i][j];
        return r;
    }

    public static double[][] Rect(int rows, int cols)
    {
        var r = new double[rows][];
        for (int i = 0; i < rows; i++) r[i] = new double[cols];
        return r;
    }

    public static double[] MatVec(double[][] a, double[] x)
    {
        int m = a.Length, n = a[0].Length;
        var r = new double[m];
        for (int i = 0; i < m; i++)
        {
            double s = 0;
            for (int j = 0; j < n; j++) s += a[i][j] * x[j];
            r[i] = s;
        }
        return r;
    }

    public static double[][] MatMul(double[][] a, double[][] b)
    {
        int m = a.Length, n = b[0].Length, t = b.Length;
        var r = Rect(m, n);
        for (int i = 0; i < m; i++)
            for (int k = 0; k < t; k++)
            {
                double aik = a[i][k];
                for (int j = 0; j < n; j++) r[i][j] += aik * b[k][j];
            }
        return r;
    }

    public static double[][] AtA(double[][] a)
    {
        int d = a.Length, k = a[0].Length;
        var r = Rect(k, k);
        for (int i = 0; i < k; i++)
            for (int j = i; j < k; j++)
            {
                double s = 0;
                for (int t = 0; t < d; t++) s += a[t][i] * a[t][j];
                r[i][j] = s; r[j][i] = s;
            }
        return r;
    }

    /// <summary>正定对称矩阵 Cholesky 下三角。半正定加 jitter 回退。</summary>
    public static double[][] Cholesky(double[][] a)
    {
        int n = a.Length;
        var l = Rect(n, n);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double s = a[i][j];
                for (int k = 0; k < j; k++) s -= l[i][k] * l[j][k];
                if (i == j)
                {
                    if (s <= 0) throw new InvalidOperationException("矩阵非正定, 无法 Cholesky 分解");
                    l[i][j] = System.Math.Sqrt(s);
                }
                else l[i][j] = s / l[j][j];
            }
        }
        return l;
    }

    public static bool TryCholesky(double[][] a, out double[][] l)
    {
        try { l = Cholesky(a); return true; }
        catch { l = Identity(a.Length); return false; }
    }

    /// <summary>对称矩阵特征值/向量(循环 Jacobi)。</summary>
    public static (double[] values, double[][] vectors) EigenSymmetric(double[][] aIn, int maxSweeps = 60)
    {
        int n = aIn.Length;
        var a = aIn.Select(r => (double[])r.Clone()).ToArray();
        var v = Identity(n);
        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p][q] * a[p][q];
            if (off < 1e-24) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p][q];
                    if (System.Math.Abs(apq) < 1e-15) continue;
                    double app = a[p][p], aqq = a[q][q];
                    double tau = (aqq - app) / (2 * apq);
                    double t = (tau >= 0 ? 1 : -1) / (System.Math.Abs(tau) + System.Math.Sqrt(1 + tau * tau));
                    double c = 1 / System.Math.Sqrt(1 + t * t), s = t * c;
                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k][p], akq = a[k][q];
                        a[k][p] = c * akp - s * akq; a[p][k] = a[k][p];
                        a[k][q] = s * akp + c * akq; a[q][k] = a[k][q];
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k][p], vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = a[i][i];
        return (vals, v);
    }

    /// <summary>解 L L^T x = b; 非正定返回 null。</summary>
    public static double[]? SolveSPD(double[][] a, double[] b, double jitter = 0)
    {
        var work = a.Select(r => (double[])r.Clone()).ToArray();
        if (jitter > 0) for (int i = 0; i < work.Length; i++) work[i][i] += jitter;
        if (!TryCholesky(work, out var l)) return null;
        int n = b.Length;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= l[i][k] * y[k];
            y[i] = s / l[i][i];
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int k = i + 1; k < n; k++) s -= l[k][i] * x[k];
            x[i] = s / l[i][i];
        }
        return x;
    }

    public static double Dot(double[] a, double[] b)
    {
        double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s;
    }

    public static double[] SolveTriDiagEquality(double[][] A, double[] b)
    {
        // 最小二乘解 A x = b (用于可识别性诊断), A: d x k
        var at = Transpose(A);
        var ata = Rect(A[0].Length, A[0].Length);
        for (int i = 0; i < ata.Length; i++)
            for (int j = 0; j < ata.Length; j++)
                for (int t = 0; t < A.Length; t++) ata[i][j] += at[i][t] * at[t][j];
        var atb = new double[A[0].Length];
        for (int i = 0; i < atb.Length; i++)
            for (int t = 0; t < A.Length; t++) atb[i] += at[i][t] * b[t];
        return SolveSPD(ata, atb, 1e-12) ?? new double[A[0].Length];
    }
}

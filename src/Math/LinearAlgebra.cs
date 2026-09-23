using System;

namespace IsoMix.MathUtils;

/// <summary>小型实对称矩阵工具：Cholesky 与 Jacobi 特征分解。</summary>
public static class LinearAlgebra
{
    public static double[][] Identity(int n)
    {
        var m = new double[n][];
        for (int i = 0; i < n; i++) { m[i] = new double[n]; m[i][i] = 1; }
        return m;
    }

    public static double[][] Copy(double[][] a)
    {
        var r = new double[a.Length][];
        for (int i = 0; i < a.Length; i++)
        {
            r[i] = new double[a[i].Length];
            Array.Copy(a[i], r[i], a[i].Length);
        }
        return r;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        int n = a.Length, m = b[0].Length, k = b.Length;
        var r = new double[n][];
        for (int i = 0; i < n; i++)
        {
            r[i] = new double[m];
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int t = 0; t < k; t++) s += a[i][t] * b[t][j];
                r[i][j] = s;
            }
        }
        return r;
    }

    public static double[] Multiply(double[][] a, double[] x)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            double s = 0;
            for (int j = 0; j < x.Length; j++) s += a[i][j] * x[j];
            r[i] = s;
        }
        return r;
    }

    public static double[][] Transpose(double[][] a)
    {
        int n = a[0].Length, m = a.Length;
        var r = new double[n][];
        for (int i = 0; i < n; i++)
        {
            r[i] = new double[m];
            for (int j = 0; j < m; j++) r[i][j] = a[j][i];
        }
        return r;
    }

    /// <summary>Cholesky 分解；非正定返回 null。</summary>
    public static double[][]? Cholesky(double[][] a)
    {
        int n = a.Length;
        var l = new double[n][];
        for (int i = 0; i < n; i++) l[i] = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i][j];
                for (int k = 0; k < j; k++) sum -= l[i][k] * l[j][k];
                if (i == j)
                {
                    if (sum <= 1e-14) return null;
                    l[i][j] = Math.Sqrt(sum);
                }
                else l[i][j] = sum / l[j][j];
            }
        }
        return l;
    }

    /// <summary>对称正定矩阵的二次型 x' A x，用 Cholesky 求解。</summary>
    public static double QuadraticForm(double[][] a, double[] x)
    {
        int n = a.Length;
        var l = Cholesky(a) ?? throw new ArgumentException("matrix not positive definite");
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = x[i];
            for (int j = 0; j < i; j++) s -= l[i][j] * y[j];
            y[i] = s / l[i][i];
        }
        double q = 0;
        foreach (double v in y) q += v * v;
        return q;
    }

    /// <summary>Jacobi 特征值分解（实对称矩阵）。返回升序特征值。</summary>
    public static double[] Eigenvalues(double[][] matrix, int maxSweeps = 100)
    {
        var a = Copy(matrix);
        int n = a.Length;
        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p][q] * a[p][q];
            if (off < 1e-26) break;
            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p][q]) < 1e-18) continue;
                    double tau = (a[q][q] - a[p][p]) / (2 * a[p][q]);
                    double t = Math.Sign(tau) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1 / Math.Sqrt(1 + t * t);
                    double s = t * c;
                    double app = a[p][p], aqq = a[q][q], apq = a[p][q];
                    a[p][p] = app - t * apq;
                    a[q][q] = aqq + t * apq;
                    a[p][q] = a[q][p] = 0;
                    for (int r = 0; r < n; r++)
                    {
                        if (r == p || r == q) continue;
                        double arp = a[r][p], arq = a[r][q];
                        a[r][p] = a[p][r] = c * arp - s * arq;
                        a[r][q] = a[q][r] = s * arp + c * arq;
                    }
                }
            }
        }
        var ev = new double[n];
        for (int i = 0; i < n; i++) ev[i] = a[i][i];
        Array.Sort(ev);
        return ev;
    }

    /// <summary>2 条件数（最大/最小特征值）。</summary>
    public static double ConditionNumber(double[][] matrix)
    {
        var ev = Eigenvalues(matrix);
        double min = Math.Abs(ev[0]);
        double max = Math.Abs(ev[^1]);
        return min < 1e-15 ? double.PositiveInfinity : max / min;
    }

    /// <summary>以容差判数值秩（基于奇异量级估计：这里用 A'A 特征值开方）。</summary>
    public static int NumericRank(double[][] a, double tolerance = 1e-9)
    {
        var ata = Multiply(Transpose(a), a);
        var ev = Eigenvalues(ata);
        double largest = Math.Sqrt(Math.Abs(ev[^1]));
        int rank = 0;
        foreach (double e in ev)
            if (Math.Sqrt(Math.Abs(e)) > tolerance * Math.Max(1.0, largest)) rank++;
        return rank;
    }
}

using System;

namespace IsotopeMixing.Core.Engine;

public static class LinearAlgebra
{
    /// <summary>对称矩阵 Jacobi 特征分解：返回 (升序特征值, 特征向量列)。</summary>
    public static (double[] values, double[,] vectors) SymmetricEigen(double[,] a, int sweeps = 60)
    {
        int n = a.GetLength(0);
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;
        var work = (double[,])a.Clone();

        for (int sweep = 0; sweep < sweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += work[p, q] * work[p, q];
            if (off < 1e-28) break;

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = work[p, q];
                    if (Math.Abs(apq) < 1e-15) continue;
                    double app = work[p, p], aqq = work[q, q];
                    double phi = 0.5 * Math.Atan2(2 * apq, aqq - app);
                    double c = Math.Cos(phi), s = Math.Sin(phi);
                    for (int k = 0; k < n; k++)
                    {
                        double akp = work[k, p], akq = work[k, q];
                        work[k, p] = c * akp - s * akq;
                        work[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double apk = work[p, k], aqk = work[q, k];
                        work[p, k] = c * apk - s * aqk;
                        work[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }
        }

        var orig = new double[n];
        for (int i = 0; i < n; i++) orig[i] = work[i, i];
        var order = Enumerable.Range(0, n).OrderBy(i => orig[i]).ToArray();
        var vals = new double[n];
        var sortedV = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            vals[j] = orig[order[j]];
            for (int i = 0; i < n; i++) sortedV[i, j] = v[i, order[j]];
        }
        return (vals, sortedV);
    }

    /// <summary>Cholesky 下三角（协方差正定）。非正定回退为特征值截断的伪分解。</summary>
    public static double[,] CholSqrt(double[,] covariance)
    {
        int n = covariance.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = covariance[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 1e-14)
                        return RegularizedSqrt(covariance);
                    l[i, j] = Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        }
        return l;
    }

    private static double[,] RegularizedSqrt(double[,] covariance)
    {
        int n = covariance.GetLength(0);
        var (vals, vecs) = SymmetricEigen(covariance);
        var l = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            double sd = Math.Sqrt(Math.Max(vals[j], 1e-12));
            for (int i = 0; i < n; i++) l[i, j] = vecs[i, j] * sd;
        }
        return l;
    }

    public static double[,] InvertSPD(double[,] covariance)
    {
        int n = covariance.GetLength(0);
        var l = CholSqrt(covariance);
        var inv = new double[n, n];
        // 解 L L^T X = I
        for (int col = 0; col < n; col++)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = i == col ? 1.0 : 0.0;
                for (int k = 0; k < i; k++) sum -= l[i, k] * y[k];
                y[i] = sum / l[i, i];
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = y[i];
                for (int k = i + 1; k < n; k++) sum -= l[k, i] * inv[k, col];
                inv[i, col] = sum / l[i, i];
            }
        }
        return inv;
    }

    public static double[] SolveSPD(double[,] spd, double[] b)
    {
        int n = b.Length;
        var inv = InvertSPD(spd);
        return Multiply(inv, b);
    }

    public static double[,] Multiply(double[,] a, double[,] b)
    {
        int n = a.GetLength(0), k = a.GetLength(1), m = b.GetLength(1);
        var r = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int t = 0; t < k; t++) s += a[i, t] * b[t, j];
                r[i, j] = s;
            }
        return r;
    }

    public static double[] Multiply(double[,] a, double[] x)
    {
        int n = a.GetLength(0), k = a.GetLength(1);
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int t = 0; t < k; t++) s += a[i, t] * x[t];
            r[i] = s;
        }
        return r;
    }

    public static double[,] Transpose(double[,] a)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var r = new double[m, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                r[j, i] = a[i, j];
        return r;
    }

    public static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}

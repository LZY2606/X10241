using System;

namespace IsoMix.Domain;

public static class MatrixOps
{
    public static double[,] Mul(double[,] a, double[,] b)
    {
        int n = a.GetLength(0), m = b.GetLength(1), p = b.GetLength(0);
        var c = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int t = 0; t < p; t++) s += a[i, t] * b[t, j];
                c[i, j] = s;
            }
        return c;
    }

    public static double[] MulVec(double[,] a, double[] x)
    {
        int n = a.GetLength(0), p = a.GetLength(1);
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int t = 0; t < p; t++) s += a[i, t] * x[t];
            y[i] = s;
        }
        return y;
    }

    public static double[,] Transpose(double[,] a)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var t = new double[m, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++) t[j, i] = a[i, j];
        return t;
    }

    public static double[,] Ata(double[,] a) => Mul(Transpose(a), a);

    /// <summary>对称矩阵 Jacobi 特征分解，返回升序特征值，特征向量按列。</summary>
    public static (double[] eigenvalues, double[,] eigenvectors) JacobiEigen(double[,] m)
    {
        int n = m.GetLength(0);
        var a = (double[,])m.Clone();
        var v = Identity(n);
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++) off += a[p, q] * a[p, q];
            if (off < 1e-26) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p, q];
                    if (Math.Abs(apq) < 1e-15) continue;
                    double app = a[p, p], aqq = a[q, q];
                    double phi = 0.5 * Math.Atan2(2 * apq, aqq - app);
                    double c = Math.Cos(phi), s = Math.Sin(phi);
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
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = a[i, i];
        Array.Sort(vals);
        return (vals, v);
    }

    public static double[,] Identity(int n)
    {
        var i = new double[n, n];
        for (int k = 0; k < n; k++) i[k, k] = 1;
        return i;
    }

    /// <summary>矩阵 A 的奇异值（利用 A^T A 的 Jacobi 特征值），降序返回。</summary>
    public static double[] SingularValues(double[,] a)
    {
        int rows = a.GetLength(0), cols = a.GetLength(1);
        double[,] gram;
        int small;
        if (rows >= cols) { gram = Ata(a); small = cols; }
        else { gram = Mul(a, Transpose(a)); small = rows; }
        var (ev, _) = JacobiEigen(gram);
        var sv = new double[Math.Max(rows, cols)];
        for (int i = 0; i < small; i++)
        {
            double e = ev[small - 1 - i];
            sv[i] = e > 0 ? Math.Sqrt(e) : 0;
        }
        return sv;
    }

    /// <summary>对称正定矩阵的 Cholesky 下三角 L（S = L L^T）。</summary>
    public static double[,] Cholesky(double[,] s)
    {
        int n = s.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                double sum = s[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 1e-14) sum = 1e-14;
                    l[i, j] = Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        return l;
    }

    public static double[,] InvertSymmetric(double[,] s)
    {
        int n = s.GetLength(0);
        var l = Cholesky(s);
        var inv = new double[n, n];
        // solve S X = I column by column via forward/back substitution
        for (int col = 0; col < n; col++)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = i == col ? 1.0 : 0.0;
                for (int k = 0; k < i; k++) sum -= l[i, k] * y[k];
                y[i] = sum / l[i, i];
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = y[i];
                for (int k = i + 1; k < n; k++) sum -= l[k, i] * x[k];
                x[i] = sum / l[i, i];
            }
            for (int i = 0; i < n; i++) inv[i, col] = x[i];
        }
        return inv;
    }

    public static double Quadratic(double[,] invS, double[] r)
    {
        int n = r.Length;
        double q = 0;
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int j = 0; j < n; j++) s += invS[i, j] * r[j];
            q += r[i] * s;
        }
        return q;
    }

    /// <summary>卡方分布自由度 1..3 在置信水平 0.95 的分位数。</summary>
    public static double ChiSquareQuantile95(int df) => df switch
    {
        1 => 3.841458820694124,
        2 => 5.991464547107979,
        3 => 7.814727903251178,
        _ => ChiSquareApprox95(df)
    };

    private static double ChiSquareApprox95(int df)
    {
        // Wilson-Hilferty: 0.95 z=1.6448536269514722
        double z = 1.6448536269514722;
        double t = 2.0 / (9.0 * df);
        double x = 1.0 - t + z * Math.Sqrt(t);
        return df * x * x * x;
    }
}

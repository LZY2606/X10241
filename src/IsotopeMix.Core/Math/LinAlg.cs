using System;

namespace IsotopeMix.Core.Math;

/// <summary>Small, dependency-free dense linear algebra for isotope mixing problems.</summary>
public static class LinAlg
{
    public static double[][] Zeros(int r, int c)
    {
        var m = new double[r][];
        for (var i = 0; i < r; i++) m[i] = new double[c];
        return m;
    }

    public static double[][] Identity(int n)
    {
        var m = Zeros(n, n);
        for (var i = 0; i < n; i++) m[i][i] = 1.0;
        return m;
    }

    public static double[][] Copy(double[][] a)
    {
        var r = a.Length;
        var c = a[0].Length;
        var m = Zeros(r, c);
        for (var i = 0; i < r; i++) Array.Copy(a[i], m[i], c);
        return m;
    }

    public static double[][] Transpose(double[][] a)
    {
        var r = a.Length;
        var c = a[0].Length;
        var t = Zeros(c, r);
        for (var i = 0; i < r; i++)
        for (var j = 0; j < c; j++)
            t[j][i] = a[i][j];
        return t;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        var r = a.Length;
        var k = b.Length;
        var c = b[0].Length;
        var m = Zeros(r, c);
        for (var i = 0; i < r; i++)
        for (var j = 0; j < c; j++)
        {
            var s = 0.0;
            for (var p = 0; p < k; p++) s += a[i][p] * b[p][j];
            m[i][j] = s;
        }
        return m;
    }

    public static double[] Multiply(double[][] a, double[] x)
    {
        var r = a.Length;
        var c = a[0].Length;
        var y = new double[r];
        for (var i = 0; i < r; i++)
        {
            var s = 0.0;
            for (var j = 0; j < c; j++) s += a[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double Dot(double[] a, double[] b)
    {
        var s = 0.0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static double[] Add(double[] a, double[] b)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++) r[i] = a[i] + b[i];
        return r;
    }

    public static double[] Subtract(double[] a, double[] b)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++) r[i] = a[i] - b[i];
        return r;
    }

    public static double[] Scale(double s, double[] a)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++) r[i] = a[i] * s;
        return r;
    }

    /// <summary>Jacobi eigendecomposition of a real symmetric matrix. Returns eigenvalues and V columns.</summary>
    public static (double[] eigenvalues, double[][] v) SymmetricEigen(double[][] aIn)
    {
        var n = aIn.Length;
        var a = Copy(aIn);
        var v = Identity(n);
        const int maxSweeps = 100;
        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
                off += System.Math.Abs(a[p][q]);
            if (off < 1e-14) break;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                var apq = a[p][q];
                if (System.Math.Abs(apq) < 1e-15) continue;
                var app = a[p][p];
                var aqq = a[q][q];
                var phi = (aqq - app) / (2.0 * apq);
                var t = phi >= 0
                    ? 1.0 / (phi + System.Math.Sqrt(1.0 + phi * phi))
                    : 1.0 / (phi - System.Math.Sqrt(1.0 + phi * phi));
                var c = 1.0 / System.Math.Sqrt(1.0 + t * t);
                var s = t * c;
                a[p][p] = app - t * apq;
                a[q][q] = aqq + t * apq;
                a[p][q] = 0;
                a[q][p] = 0;
                for (var k = 0; k < n; k++)
                {
                    if (k == p || k == q) continue;
                    var akp = a[k][p];
                    var akq = a[k][q];
                    a[k][p] = c * akp - s * akq;
                    a[p][k] = a[k][p];
                    a[k][q] = s * akp + c * akq;
                    a[q][k] = a[k][q];
                }
                for (var k = 0; k < n; k++)
                {
                    var vkp = v[k][p];
                    var vkq = v[k][q];
                    v[k][p] = c * vkp - s * vkq;
                    v[k][q] = s * vkp + c * vkq;
                }
            }
        }
        var ev = new double[n];
        for (var i = 0; i < n; i++) ev[i] = a[i][i];
        // sort descending
        var idx = new int[n];
        for (var i = 0; i < n; i++) idx[i] = i;
        Array.Sort(idx, (x, y) => ev[y].CompareTo(ev[x]));
        var sorted = new double[n];
        var vs = Zeros(n, n);
        for (var i = 0; i < n; i++)
        {
            sorted[i] = ev[idx[i]];
            for (var k = 0; k < n; k++) vs[k][i] = v[k][idx[i]];
        }
        return (sorted, vs);
    }

    /// <summary>Singular values and right singular vectors V of matrix A, via A^T A eigen decomposition.</summary>
    public static (double[] singularValues, double[][] v) SvdRight(double[][] a)
    {
        var ata = Multiply(Transpose(a), a);
        var (ev, v) = SymmetricEigen(ata);
        for (var i = 0; i < ev.Length; i++) ev[i] = ev[i] > 0 ? System.Math.Sqrt(ev[i]) : 0.0;
        return (ev, v);
    }

    public static double FrobeniusNorm(double[][] a)
    {
        var s = 0.0;
        foreach (var row in a)
            foreach (var x in row)
                s += x * x;
        return System.Math.Sqrt(s);
    }

    /// <summary>Cholesky factor L (A = L L^T) for a symmetric positive definite matrix, with tiny jitter fallback.</summary>
    public static double[][] Cholesky(double[][] aIn)
    {
        var n = aIn.Length;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var a = Copy(aIn);
            if (attempt > 0)
            {
                var jitter = System.Math.Pow(10, -12 + attempt * 2);
                for (var i = 0; i < n; i++) a[i][i] += jitter;
            }
            var l = Zeros(n, n);
            var ok = true;
            for (var i = 0; i < n && ok; i++)
            {
                for (var j = 0; j <= i; j++)
                {
                    var sum = a[i][j];
                    for (var k = 0; k < j; k++) sum -= l[i][k] * l[j][k];
                    if (i == j)
                    {
                        if (sum <= 0) { ok = false; break; }
                        l[i][j] = System.Math.Sqrt(sum);
                    }
                    else
                    {
                        l[i][j] = sum / l[j][j];
                    }
                }
            }
            if (ok) return l;
        }
        throw new InvalidOperationException("Covariance matrix is not positive definite even with jitter.");
    }

    /// <summary>Solve a symmetric positive definite system A x = b via Cholesky.</summary>
    public static double[] SolveSpd(double[][] a, double[] b)
    {
        var l = Cholesky(a);
        return SolveLLt(l, b);
    }

    public static double[] SolveLLt(double[][] l, double[] b)
    {
        var n = l.Length;
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = b[i];
            for (var k = 0; k < i; k++) s -= l[i][k] * y[k];
            y[i] = s / l[i][i];
        }
        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var s = y[i];
            for (var k = i + 1; k < n; k++) s -= l[k][i] * x[k];
            x[i] = s / l[i][i];
        }
        return x;
    }

    /// <summary>Mahalanobis squared distance d^T Sigma^{-1} d (Sigma propagated through Sigma, no scalar inflation).</summary>
    public static double MahalanobisSq(double[] d, double[][] sigma)
    {
        var x = SolveSpd(sigma, d);
        return Dot(d, x);
    }

    /// <summary>Regularised inverse of a covariance matrix, based on its eigen decomposition.</summary>
    public static double[][] CovarianceInverse(double[][] sigma)
    {
        var n = sigma.Length;
        var (ev, v) = SymmetricEigen(sigma);
        var inv = Zeros(n, n);
        for (var i = 0; i < n; i++)
        {
            var lambda = ev[i] <= 1e-18 ? 1e-18 : ev[i];
            var invLambda = 1.0 / lambda;
            for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++)
                inv[r][c] += v[r][i] * invLambda * v[c][i];
        }
        return inv;
    }
}

namespace IsotopeMixing.Core.MathX;

public static class LinearAlgebra
{
    public static double[][] Zeros(int r, int c)
    {
        var m = new double[r][];
        for (var i = 0; i < r; i++) m[i] = new double[c];
        return m;
    }

    public static double[][] Mat(int r, int c, Func<int, int, double> f)
    {
        var m = new double[r][];
        for (var i = 0; i < r; i++) { m[i] = new double[c]; for (var j = 0; j < c; j++) m[i][j] = f(i, j); }
        return m;
    }

    public static double[] Vec(int n, Func<int, double> f)
    {
        var v = new double[n];
        for (var i = 0; i < n; i++) v[i] = f(i);
        return v;
    }

    public static double[][] Transpose(double[][] a)
    {
        var r = a.Length; var c = a[0].Length;
        return Mat(c, r, (i, j) => a[j][i]);
    }

    public static double[][] Mult(double[][] a, double[][] b)
    {
        var r = a.Length; var k = a[0].Length; var c = b[0].Length;
        var m = Zeros(r, c);
        for (var i = 0; i < r; i++)
            for (var t = 0; t < k; t++)
            {
                var aik = a[i][t];
                for (var j = 0; j < c; j++) m[i][j] += aik * b[t][j];
            }
        return m;
    }

    public static double[] Mult(double[][] a, double[] x)
    {
        var r = a.Length; var c = a[0].Length;
        var y = new double[r];
        for (var i = 0; i < r; i++) { double s = 0; for (var j = 0; j < c; j++) s += a[i][j] * x[j]; y[i] = s; }
        return y;
    }

    /// <summary>Cholesky 求解对称正定系统 A x = b；非正定返回 null。</summary>
    public static double[]? SolvePositiveDefinite(double[][] a, double[] b)
    {
        var n = a.Length;
        var l = Zeros(n, n);
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                double s = a[i][j];
                for (var k = 0; k < j; k++) s -= l[i][k] * l[j][k];
                if (i == j)
                {
                    if (s <= 1e-12) return null;
                    l[i][j] = Math.Sqrt(s);
                }
                else l[i][j] = s / l[j][j];
            }
        }
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            double s = b[i];
            for (var k = 0; k < i; k++) s -= l[i][k] * y[k];
            y[i] = s / l[i][i];
        }
        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (var k = i + 1; k < n; k++) s -= l[k][i] * x[k];
            x[i] = s / l[i][i];
        }
        return x;
    }

    /// <summary>对称矩阵 Jacobi 特征分解（小矩阵用），升序返回特征值。</summary>
    public static (double[] values, double[][] vectors) EigSymmetric(double[][] aIn)
    {
        var n = aIn.Length;
        var a = aIn.Select(r => (double[])r.Clone()).ToArray();
        var v = Identity(n);
        for (var sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++) for (var q = p + 1; q < n; q++) off += a[p][q] * a[p][q];
            if (off < 1e-24) break;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p][q]) < 1e-14) continue;
                    var tau = (a[q][q] - a[p][p]) / (2 * a[p][q]);
                    var t = (tau >= 0 ? 1 : -1) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    var c = 1 / Math.Sqrt(1 + t * t);
                    var s = t * c;
                    for (var k = 0; k < n; k++)
                    {
                        var akp = a[k][p]; var akq = a[k][q];
                        a[k][p] = c * akp - s * akq;
                        a[k][q] = s * akp + c * akq;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var apk = a[p][k]; var aqk = a[q][k];
                        a[p][k] = c * apk - s * aqk;
                        a[q][k] = s * apk + c * aqk;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var vkp = v[k][p]; var vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
        }
        var vals = new double[n];
        for (var i = 0; i < n; i++) vals[i] = a[i][i];
        var idx = Enumerable.Range(0, n).OrderBy(i => vals[i]).ToArray();
        var sortedVals = idx.Select(i => vals[i]).ToArray();
        var sortedVecs = Mat(n, n, (i, j) => v[i][idx[j]]);
        return (sortedVals, sortedVecs);
    }

    public static double[][] Identity(int n) => Mat(n, n, (i, j) => i == j ? 1.0 : 0.0);

    /// <summary>
    /// 单侧 Jacobi SVD：A = U S Vt。返回 U(m x k)、奇异值、Vt(k x n)，k=min(m,n)。
    /// </summary>
    public static (double[][] u, double[] s, double[][] vt) Svd(double[][] a)
    {
        var m = a.Length; var n = a[0].Length;
        var v = Identity(n);
        var work = a.Select(r => (double[])r.Clone()).ToArray();
        for (var sweep = 0; sweep < 60; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                {
                    double pp = 0, qq = 0, pq = 0;
                    for (var i = 0; i < m; i++) { pp += work[i][p] * work[i][p]; qq += work[i][q] * work[i][q]; pq += work[i][p] * work[i][q]; }
                    off += pq * pq;
                    if (Math.Abs(pq) < 1e-14 * Math.Sqrt(pp * qq + 1e-30)) continue;
                    var zeta = (qq - pp) / (2 * pq);
                    var t = (zeta >= 0 ? 1 : -1) / (Math.Abs(zeta) + Math.Sqrt(1 + zeta * zeta));
                    var c = 1 / Math.Sqrt(1 + t * t);
                    var s = t * c;
                    for (var i = 0; i < m; i++)
                    {
                        var x = work[i][p]; var y = work[i][q];
                        work[i][p] = c * x - s * y;
                        work[i][q] = s * x + c * y;
                    }
                    for (var i = 0; i < n; i++)
                    {
                        var x = v[i][p]; var y = v[i][q];
                        v[i][p] = c * x - s * y;
                        v[i][q] = s * x + c * y;
                    }
                }
            if (off < 1e-24) break;
        }
        var cols = new (double norm, int idx)[n];
        for (var j = 0; j < n; j++)
        {
            double nm = 0;
            for (var i = 0; i < m; i++) nm += work[i][j] * work[i][j];
            cols[j] = (Math.Sqrt(nm), j);
        }
        Array.Sort(cols, (x, y) => y.norm.CompareTo(x.norm));
        var k = Math.Min(m, n);
        var s = new double[k];
        var u = Zeros(m, k);
        var vt = Zeros(k, n);
        for (var t = 0; t < k; t++)
        {
            var j = cols[t].idx;
            s[t] = cols[t].norm;
            for (var i = 0; i < m; i++) u[i][t] = s[t] > 1e-14 ? work[i][j] / s[t] : 0;
            for (var i = 0; i < n; i++) vt[t][i] = v[i][j];
            if (s[t] <= 1e-14)
            {
                // 零奇异值方向：任意单位向量（用 V 列即可，已正交）
                for (var i = 0; i < m; i++) u[i][t] = 0;
            }
        }
        return (u, s, vt);
    }

    /// <summary>Moore-Penrose 伪逆（经 SVD）。</summary>
    public static double[][] PseudoInverse(double[][] a, double? tol = null)
    {
        var (u, s, vt) = Svd(a);
        var m = a.Length; var n = a[0].Length;
        var t = tol ?? Math.Max(m, n) * s[0] * 1e-11;
        var pinv = Zeros(n, m);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] <= t) continue;
            var f = 1.0 / s[i];
            for (var r = 0; r < n; r++)
                for (var c = 0; c < m; c++)
                    pinv[r][c] += f * vt[i][r] * u[c][i];
        }
        return pinv;
    }

    public static double ChiSquaredCriticalUpper095(int dof)
    {
        // 常用自由度的 0.95 分位数表；超出范围用 Wilson-Hilferty 近似
        double[] table = { 3.841, 5.991, 7.815, 9.488, 11.070, 12.592, 14.067, 15.507, 16.919, 18.307, 25.0, 27.59, 30.14, 32.67, 35.17 };
        if (dof >= 1 && dof <= table.Length) return table[dof - 1];
        var z = 1.6448536269514722;
        var h = 1 - 2.0 / (9 * dof) + z * Math.Sqrt(2.0 / (9 * dof));
        return dof * h * h * h;
    }
}

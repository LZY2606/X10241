using System;

namespace IsotopeMixing.Web.Engine;

// 小型线性代数工具：仅对称矩阵特征分解（Jacobi）与紧凑 SVD（用 A^T A 特征分解），
// 规模 <= 端元数(4) x 同位素维(3)，实现简单且确定性。
public static class Linalg
{
    // 对称矩阵 Jacobi 特征分解。返回 (values, vectors-columnwise)。
    public static (double[] values, double[][] vectors) SymmetricEigen(double[][] a, int maxSweeps = 60)
    {
        int n = a.Length;
        var v = Identity(n);
        var w = a.Select(r => r.ToArray()).ToArray();

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += Math.Abs(w[p][q]);
            if (off < 1e-14) break;

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = w[p][q];
                    if (Math.Abs(apq) < 1e-15) continue;
                    double app = w[p][p];
                    double aqq = w[q][q];
                    double phi = Math.Abs(app - aqq) < 1e-300
                        ? Math.PI / 4
                        : 0.5 * Math.Atan2(2 * apq, app - aqq);
                    double c = Math.Cos(phi);
                    double s = Math.Sin(phi);

                    for (int k = 0; k < n; k++)
                    {
                        double wpk = w[p][k];
                        double wqk = w[q][k];
                        w[p][k] = c * wpk - s * wqk;
                        w[q][k] = s * wpk + c * wqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double wkp = w[k][p];
                        double wkq = w[k][q];
                        w[k][p] = c * wkp - s * wkq;
                        w[k][q] = s * wkp + c * wkq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k][p];
                        double vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
            }
        }

        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = w[i][i];
        // 降序排列（大奇异值在前）。
        var order = Enumerable.Range(0, n).OrderByDescending(i => vals[i]).ToArray();
        var sortedVals = order.Select(i => vals[i]).ToArray();
        var sortedVecs = new double[n][];
        for (int i = 0; i < n; i++)
        {
            sortedVecs[i] = new double[n];
            for (int j = 0; j < n; j++) sortedVecs[i][j] = v[i][order[j]];
        }
        // 数值上的负特征值钳到 0（仅用于几何，不改变最优化）。
        for (int i = 0; i < n; i++)
            if (sortedVals[i] < 0 && sortedVals[i] > -1e-9) sortedVals[i] = 0;
        return (sortedVals, sortedVecs);
    }

    // 紧凑 SVD：A (k x m) -> U(k x r), sigma(r), Vt(r x m)，r=min(k,m)。
    public static (double[][] u, double[] s, double[][] vt) Svd(double[][] a)
    {
        int k = a.Length;
        int m = a[0].Length;
        var ata = Mul(Transpose(a), a); // m x m
        var (vals, vecs) = SymmetricEigen(ata); // vecs columns: m x m
        int r = Math.Min(k, m);
        var sig = new double[r];
        var vCols = new double[r][];
        for (int i = 0; i < r; i++)
        {
            sig[i] = Math.Sqrt(Math.Max(0, vals[i]));
            var col = new double[m];
            for (int row = 0; row < m; row++) col[row] = vecs[row][i];
            vCols[i] = col;
        }
        var vt = new double[r][];
        var u = new double[k][];
        for (int i = 0; i < k; i++) u[i] = new double[r];
        for (int i = 0; i < r; i++)
        {
            vt[i] = vCols[i];
            if (sig[i] > 1e-12)
            {
                var avi = MulVec(a, vCols[i]);
                for (int row = 0; row < k; row++) u[row][i] = avi[row] / sig[i];
            }
            else
            {
                // 与已有列正交化
                var basis = new double[k];
                basis[i % k] = 1;
                for (int j = 0; j < i; j++)
                {
                    var uj = Enumerable.Range(0, k).Select(row => u[row][j]).ToArray();
                    double d = Dot(basis, uj);
                    for (int row = 0; row < k; row++) basis[row] -= d * uj[row];
                }
                double nrm = Norm(basis);
                if (nrm < 1e-12)
                {
                    basis = new double[k];
                    basis[0] = 1;
                    for (int j = 0; j < i; j++)
                    {
                        var uj = Enumerable.Range(0, k).Select(row => u[row][j]).ToArray();
                        double d = Dot(basis, uj);
                        for (int row = 0; row < k; row++) basis[row] -= d * uj[row];
                    }
                    nrm = Norm(basis);
                }
                for (int row = 0; row < k; row++) u[row][i] = basis[row] / nrm;
            }
        }
        return (u, sig, vt);
    }

    public static double[][] Identity(int n)
    {
        var m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[n];
            m[i][i] = 1;
        }
        return m;
    }

    public static double[][] Transpose(double[][] a)
    {
        int k = a.Length, m = a[0].Length;
        var t = new double[m][];
        for (int j = 0; j < m; j++)
        {
            t[j] = new double[k];
            for (int i = 0; i < k; i++) t[j][i] = a[i][j];
        }
        return t;
    }

    public static double[][] Mul(double[][] a, double[][] b)
    {
        int n = a.Length, p = b.Length, q = b[0].Length;
        var c = new double[n][];
        for (int i = 0; i < n; i++)
        {
            c[i] = new double[q];
            for (int j = 0; j < q; j++)
            {
                double s = 0;
                for (int t = 0; t < p; t++) s += a[i][t] * b[t][j];
                c[i][j] = s;
            }
        }
        return c;
    }

    public static double[] MulVec(double[][] a, double[] x)
    {
        int n = a.Length, m = a[0].Length;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int j = 0; j < m; j++) s += a[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static double Norm(double[] a) => Math.Sqrt(Dot(a, a));

    // 正定对称矩阵求逆（Cholesky）。
    public static double[][] InvertPositiveDefinite(double[][] a)
    {
        int n = a.Length;
        var l = new double[n][];
        for (int i = 0; i < n; i++) l[i] = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i][j];
                for (int t = 0; t < j; t++) sum -= l[i][t] * l[j][t];
                if (i == j)
                {
                    if (sum <= 1e-18) throw new InvalidOperationException("协方差矩阵非正定，无法传播相关误差。");
                    l[i][j] = Math.Sqrt(sum);
                }
                else l[i][j] = sum / l[j][j];
            }
        }
        var inv = new double[n][];
        for (int i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            // 解 L y = e_i
            var y = new double[n];
            for (int row = 0; row < n; row++)
            {
                double s = row == i ? 1.0 : 0.0;
                for (int t = 0; t < row; t++) s -= l[row][t] * y[t];
                y[row] = s / l[row][row];
            }
            // 解 L^T x = y
            for (int row = n - 1; row >= 0; row--)
            {
                double s = y[row];
                for (int t = row + 1; t < n; t++) s -= l[t][row] * inv[t][i];
                inv[row][i] = s / l[row][row];
            }
        }
        return inv;
    }
}

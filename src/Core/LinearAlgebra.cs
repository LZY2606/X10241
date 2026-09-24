using System.Globalization;

namespace IsotopeMixing.Core;

/// <summary>轻量线性代数工具：所有混合模型计算均不依赖外部数值库，保证可重放与可审计。</summary>
public static class LinearAlgebra
{
    /// <summary>高斯消元求解 A x = b（部分主元）。失败返回 false。</summary>
    public static bool Solve(double[][] a, double[] b, out double[] x)
    {
        int n = a.Length;
        double[][] m = a.Select(r => (double[])r.Clone()).ToArray();
        double[] rhs = (double[])b.Clone();

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            double best = Math.Abs(m[col][col]);
            for (int r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r][col]) > best)
                {
                    best = Math.Abs(m[r][col]);
                    pivot = r;
                }
            }

            if (best < 1e-14)
            {
                x = System.Array.Empty<double>();
                return false;
            }

            if (pivot != col)
            {
                (m[col], m[pivot]) = (m[pivot], m[col]);
                (rhs[col], rhs[pivot]) = (rhs[pivot], rhs[col]);
            }

            for (int r = col + 1; r < n; r++)
            {
                double factor = m[r][col] / m[col][col];
                if (factor == 0) continue;
                for (int c = col; c < n; c++) m[r][c] -= factor * m[col][c];
                rhs[r] -= factor * rhs[col];
            }
        }

        x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double sum = rhs[r];
            for (int c = r + 1; c < n; c++) sum -= m[r][c] * x[c];
            x[r] = sum / m[r][r];
        }
        return true;
    }

    /// <summary>对称正定求逆（走 Cholesky），失败退回一般逆。</summary>
    public static double[][] Inverse(double[][] a)
    {
        int n = a.Length;
        double[][] inv = new double[n][];
        for (int i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            double[] e = new double[n];
            e[i] = 1.0;
            if (!Solve(a, e, out double[] col))
                throw new InvalidOperationException("矩阵奇异，无法求逆");
            for (int r = 0; r < n; r++) inv[r][i] = col[r];
        }
        return inv;
    }

    /// <summary>求解对称正定线性系统（Cholesky），失败返回 false。</summary>
    public static bool SolveSpd(double[][] a, double[] b, out double[] x)
    {
        int n = a.Length;
        double[][] l = new double[n][];
        for (int i = 0; i < n; i++) l[i] = new double[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i][j];
                for (int p = 0; p < j; p++) sum -= l[i][p] * l[j][p];
                if (i == j)
                {
                    if (sum <= 1e-18)
                    {
                        x = System.Array.Empty<double>();
                        return false;
                    }
                    l[i][j] = Math.Sqrt(sum);
                }
                else l[i][j] = sum / l[j][j];
            }
        }

        x = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = b[i];
            for (int p = 0; p < i; p++) sum -= l[i][p] * x[p];
            x[i] = sum / l[i][i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = x[i];
            for (int p = i + 1; p < n; p++) sum -= l[p][i] * x[p];
            x[i] = sum / l[i][i];
        }
        return true;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        int n = a.Length, k = b.Length, m = b[0].Length;
        double[][] c = new double[n][];
        for (int i = 0; i < n; i++)
        {
            c[i] = new double[m];
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int p = 0; p < k; p++) s += a[i][p] * b[p][j];
                c[i][j] = s;
            }
        }
        return c;
    }

    public static double[] MultiplyVec(double[][] a, double[] x)
    {
        int n = a.Length, m = a[0].Length;
        double[] y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int j = 0; j < m; j++) s += a[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double[][] Transpose(double[][] a)
    {
        int n = a.Length, m = a[0].Length;
        double[][] t = new double[m][];
        for (int i = 0; i < m; i++)
        {
            t[i] = new double[n];
            for (int j = 0; j < n; j++) t[i][j] = a[j][i];
        }
        return t;
    }

    /// <summary>对称矩阵特征值/特征向量（Jacobi 迭代），结果按特征值升序。</summary>
    public static (double[] values, double[][] vectors) EigenSymmetric(double[][] aIn)
    {
        int n = aIn.Length;
        double[][] a = aIn.Select(r => (double[])r.Clone()).ToArray();
        double[][] v = Identity(n);

        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p][q] * a[p][q];
            if (off < 1e-28) break;

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p][q];
                    if (Math.Abs(apq) < 1e-16) continue;
                    double app = a[p][p], aqq = a[q][q];
                    double tau = (aqq - app) / (2 * apq);
                    double t = (tau >= 0 ? 1 : -1) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1 / Math.Sqrt(1 + t * t);
                    double s = t * c;

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
                    for (int r = 0; r < n; r++)
                    {
                        double vrp = v[r][p], vrq = v[r][q];
                        v[r][p] = c * vrp - s * vrq;
                        v[r][q] = s * vrp + c * vrq;
                    }
                }
            }
        }

        int[] order = Enumerable.Range(0, n).OrderBy(i => a[i][i]).ToArray();
        double[] values = order.Select(i => a[i][i]).ToArray();
        double[][] vectors = new double[n][];
        for (int col = 0; col < n; col++)
        {
            for (int r = 0; r < n; r++)
            {
                vectors[r] ??= new double[n];
                vectors[r][col] = v[r][order[col]];
            }
        }
        return (values, vectors);
    }

    /// <summary>矩阵奇异值（升序），通过 A^T A 的特征值获得。</summary>
    public static double[] SingularValues(double[][] a)
    {
        double[][] ata = Multiply(Transpose(a), a);
        (double[] eig, _) = EigenSymmetric(ata);
        return eig.Select(e => Math.Sqrt(Math.Max(0, e))).ToArray();
    }

    public static double[][] Identity(int n)
    {
        double[][] id = new double[n][];
        for (int i = 0; i < n; i++)
        {
            id[i] = new double[n];
            id[i][i] = 1;
        }
        return id;
    }

    /// <summary>二维单调链凸包，返回逆时针顶点索引。</summary>
    public static List<int> ConvexHull2D(IReadOnlyList<(double X, double Y)> pts)
    {
        int n = pts.Count;
        if (n <= 2) return Enumerable.Range(0, n).ToList();
        int[] order = Enumerable.Range(0, n)
            .OrderBy(i => pts[i].X).ThenBy(i => pts[i].Y).ToArray();

        static double Cross(IReadOnlyList<(double X, double Y)> p, int a, int b, int c) =>
            (p[b].X - p[a].X) * (p[c].Y - p[a].Y) -
            (p[b].Y - p[a].Y) * (p[c].X - p[a].X);

        List<int> lower = new();
        foreach (int i in order)
        {
            while (lower.Count >= 2 && Cross(pts, lower[^2], lower[^1], i) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(i);
        }
        List<int> upper = new();
        for (int oi = order.Length - 1; oi >= 0; oi--)
        {
            int i = order[oi];
            while (upper.Count >= 2 && Cross(pts, upper[^2], upper[^1], i) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(i);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    public static string Fmt(double v) => v.ToString("G8", CultureInfo.InvariantCulture);
}

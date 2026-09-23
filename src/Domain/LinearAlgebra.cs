namespace IsotopeMix.Domain;

/// <summary>
/// 仅依赖框架的小型线性代数工具：对称矩阵 Jacobi 特征分解、Cholesky、
/// 由 A^T A 特征分解得到的奇异值/条件数。用于可识别性判定与相关误差传播，
/// 不引入外部数值库，保证离线 restore。
/// </summary>
public static class LinearAlgebra
{
    public sealed record Eigensystem(double[] Values, double[,] Vectors);

    /// <summary>实对称矩阵的 Jacobi 特征分解，特征值升序，向量按列存放。</summary>
    public static Eigensystem SymmetricEigen(double[,] a, int maxSweeps = 100)
    {
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) throw new ArgumentException("matrix must be square");
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;
        var d = new double[n];
        var b = new double[n];
        var z = new double[n];
        for (int p = 0; p < n; p++) { d[p] = a[p, p]; b[p] = d[p]; }

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++)
                    off += Math.Abs(a[p, q]);
            if (off < 1e-14) break;
            double threshold = sweep < 3 ? 0.2 * off / (n * n) : 0.0;
            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p, q];
                    double eigenGap = d[q] - d[p];
                    if (sweep > 3 && Math.Abs(apq) < 1e-14 * (Math.Abs(d[p]) + Math.Abs(d[q])))
                    {
                        a[p, q] = 0;
                        continue;
                    }
                    double t;
                    double theta = (sweep > 3 || Math.Abs(eigenGap) > Math.Abs(apq) * 40)
                        ? 0.5 * eigenGap / apq
                        : 1.0 / (2.0 * Math.Sign(apq));
                    if (sweep > 3 || Math.Abs(eigenGap) > Math.Abs(apq) * 40)
                        t = 1.0 / (Math.Abs(theta) + Math.Sqrt(1.0 + theta * theta));
                    else
                        t = Math.Sign(apq) * (Math.Abs(theta) - Math.Sqrt(1.0 + theta * theta));
                    if (theta < 0) t = -t;
                    double c = 1.0 / Math.Sqrt(1 + t * t);
                    double s = t * c;
                    double tau = s / (1 + c);
                    double h = t * apq;
                    z[p] -= h; z[q] += h; d[p] -= h; d[q] += h;
                    a[p, q] = 0;
                    for (int j = 0; j < p; j++) Rotate(a, j, p, j, q, s, tau);
                    for (int j = p + 1; j < q; j++) Rotate(a, p, j, j, q, s, tau);
                    for (int j = q + 1; j < n; j++) Rotate(a, p, j, q, j, s, tau);
                    for (int j = 0; j < n; j++)
                    {
                        double g = v[j, p];
                        h = v[j, q];
                        v[j, p] = g - s * (h + g * tau);
                        v[j, q] = h + s * (g - h * tau);
                    }
                }
            }
            for (int p = 0; p < n; p++) { b[p] += z[p]; d[p] = b[p]; z[p] = 0; }
        }
        Array.Sort(d, Enumerable.Range(0, n).ToArray());
        var sorted = new double[n, n];
        var order = Enumerable.Range(0, n).OrderBy(i => d[i]).ToArray();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                sorted[j, i] = v[j, order[i]];
        return new Eigensystem(d, sorted);
    }

    private static void Rotate(double[,] a, int p, int q, int r, int s, double angle, double tau)
    {
        double g = a[p, q];
        double h = a[r, s];
        a[p, q] = g - angle * (h + g * tau);
        a[r, s] = h + angle * (g - h * tau);
    }

    /// <summary>返回下三角 L（A = L L^T）；非正定时返回 null。</summary>
    public static double[,]? Cholesky(double[,] a)
    {
        int n = a.GetLength(0);
        var l = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 1e-12) return null;
                    l[i, j] = Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        }
        return l;
    }

    /// <summary>求解 L L^T x = b；失败（半正定/奇异）时回退到特征值截断伪逆。</summary>
    public static double[] QuadraticSolve(double[,] matrix, double[] b)
    {
        int n = b.Length;
        var l = Cholesky(matrix);
        if (l is not null)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = b[i];
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
            return x;
        }
        return PseudoInverseMultiply(matrix, b);
    }

    public static double[] PseudoInverseMultiply(double[,] matrix, double[] b)
    {
        int n = b.Length;
        var copy = Copy(matrix);
        var eig = SymmetricEigen(copy);
        double scale = eig.Values.Max() * 1e-10;
        var x = new double[n];
        for (int col = 0; col < n; col++)
        {
            double inv = eig.Values[col] > scale ? 1.0 / eig.Values[col] : 0.0;
            double dot = 0;
            for (int i = 0; i < n; i++) dot += eig.Vectors[i, col] * b[i];
            for (int i = 0; i < n; i++) x[i] += eig.Vectors[i, col] * inv * dot;
        }
        return x;
    }


    public static double[,] PseudoInverseMatrix(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var copy = Copy(matrix);
        var eig = SymmetricEigen(copy);
        double scale = eig.Values.Max() * 1e-10;
        var inv = new double[n, n];
        for (int col = 0; col < n; col++)
        {
            double eigenvalue = eig.Values[col] > scale ? 1.0 / eig.Values[col] : 0.0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    inv[i, j] += eig.Vectors[i, col] * eig.Vectors[j, col] * eigenvalue;
        }
        return inv;
    }

    public static double LogDeterminant(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var l = Cholesky(matrix);
        if (l is not null)
        {
            double log = 0;
            for (int i = 0; i < n; i++) log += Math.Log(l[i, i]);
            return 2 * log;
        }
        var eig = SymmetricEigen(Copy(matrix));
        double floor = eig.Values.Max() * 1e-12;
        double logDet = 0;
        for (int i = 0; i < n; i++) logDet += Math.Log(Math.Max(eig.Values[i], floor));
        return logDet;
    }

    /// <summary>
    /// 通过 (A^T A) 的特征分解求奇异值（升序）、秩、条件数与右零空间基。
    /// 零空间向量用于说明"无法唯一决定"的方向。
    /// </summary>
    public static SingularInfo SvdViaNormalEquations(double[,] a, double tolerance = 1e-10)
    {
        int rows = a.GetLength(0), cols = a.GetLength(1);
        var ata = new double[cols, cols];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
            {
                double sum = 0;
                for (int k = 0; k < rows; k++) sum += a[k, i] * a[k, j];
                ata[i, j] = sum;
            }
        var eig = SymmetricEigen(ata);
        var singular = eig.Values.Select(v => v > 0 ? Math.Sqrt(v) : 0).ToArray();
        double smax = singular.Max();
        double tol = Math.Max(rows, cols) * smax * tolerance;
        int rank = singular.Count(v => v > tol);
        double smin = singular.FirstOrDefault(v => v > tol);
        var nullspace = new List<double[]>();
        for (int col = 0; col < cols; col++)
        {
            if (singular[col] <= tol)
            {
                var vec = new double[cols];
                for (int i = 0; i < cols; i++) vec[i] = eig.Vectors[i, col];
                nullspace.Add(vec);
            }
        }
        return new SingularInfo(singular, rank, smax / Math.Max(smin, 1e-300), smin, nullspace);
    }

    public static double[,] Copy(double[,] a)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var r2 = new double[r, c];
        Array.Copy(a, r2, a.Length);
        return r2;
    }

    public static double[,] Add(double[,] a, double[,] b)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var r2 = new double[r, c];
        for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) r2[i, j] = a[i, j] + b[i, j];
        return r2;
    }

    public static double[] Multiply(double[,] a, double[] x)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var y = new double[r];
        for (int i = 0; i < r; i++)
        {
            double sum = 0;
            for (int j = 0; j < c; j++) sum += a[i, j] * x[j];
            y[i] = sum;
        }
        return y;
    }
}

public sealed record SingularInfo(
    double[] ValuesAscending, int Rank, double Condition, double SmallestSingular,
    IReadOnlyList<double[]> Nullspace);


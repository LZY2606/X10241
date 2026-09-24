namespace IsotopeMix.Domain;

/// <summary>极简稠密线性代数：高斯消元 + Jacobi 对称特征分解，供 SVD 与 QP 使用。</summary>
public static class LinearAlgebra
{
    public static double[][] Matrix(int r, int c, double fill = 0)
    {
        var m = new double[r][];
        for (int i = 0; i < r; i++) { m[i] = new double[c]; for (int j = 0; j < c; j++) m[i][j] = fill; }
        return m;
    }

    public static double[][] Transpose(double[][] a)
    {
        int r = a.Length, c = a[0].Length;
        var t = Matrix(c, r);
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                t[j][i] = a[i][j];
        return t;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        int r = a.Length, k = b.Length, c = b[0].Length;
        var m = Matrix(r, c);
        for (int i = 0; i < r; i++)
            for (int t = 0; t < k; t++)
            {
                double aik = a[i][t];
                for (int j = 0; j < c; j++) m[i][j] += aik * b[t][j];
            }
        return m;
    }

    public static double[] Multiply(double[][] a, double[] x)
    {
        int r = a.Length, c = a[0].Length;
        var y = new double[r];
        for (int i = 0; i < r; i++)
        {
            double s = 0;
            for (int j = 0; j < c; j++) s += a[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double[][] SymmetricJacobi(double[][] aIn, int maxSweeps = 100)
    {
        int n = aIn.Length;
        var a = aIn.Select(r => (double[])r.Clone()).ToArray();
        var v = Matrix(n, n);
        for (int i = 0; i < n; i++) v[i][i] = 1;

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p][q] * a[p][q];
            if (off < 1e-22) break;

            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p][q];
                    if (Math.Abs(apq) < 1e-15) continue;
                    double app = a[p][p], aqq = a[q][q];
                    double phi = 0.5 * Math.Atan2(2 * apq, aqq - app);
                    double c = Math.Cos(phi), s = Math.Sin(phi);

                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k][p], akq = a[k][q];
                        a[k][p] = c * akp - s * akq;
                        a[k][q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[p][k], aqk = a[q][k];
                        a[p][k] = c * apk - s * aqk;
                        a[q][k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k][p], vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
        }

        var result = Matrix(n, n + 1);
        for (int i = 0; i < n; i++)
        {
            result[i][0] = Math.Abs(a[i][i]) < 1e-12 ? 0 : a[i][i];
            for (int j = 0; j < n; j++) result[i][j + 1] = v[j][i];
        }
        // 按特征值降序
        return result.OrderByDescending(row => row[0]).ToArray();
    }

    /// <summary>
    /// 经济型 SVD：返回奇异值降序与右奇异向量 V。
    /// 通过 AᵀA 的 Jacobi 分解得到；负特征值按 0 处理。
    /// </summary>
    public static (double[] singularValues, double[][] v) Svd(double[][] a)
    {
        var ata = Multiply(Transpose(a), a);
        var eig = SymmetricJacobi(ata);
        int n = ata.Length;
        var sv = new double[n];
        var v = Matrix(n, n);
        for (int i = 0; i < n; i++)
        {
            double lambda = eig[i][0];
            sv[i] = lambda <= 0 ? 0 : Math.Sqrt(lambda);
            for (int j = 0; j < n; j++) v[j][i] = eig[i][j + 1];
        }
        return (sv, v);
    }

    /// <summary>正定 Cholesky 求解 A x = b，失败返回 false（调用方改走 SVD/伪逆保护路径）。</summary>
    public static bool SolvePositiveDefinite(double[][] a, double[] b, out double[] x)
    {
        int n = a.Length;
        x = new double[n];
        var l = Matrix(n, n);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = 0;
                for (int k = 0; k < j; k++) sum += l[i][k] * l[j][k];
                if (i == j)
                {
                    double diag = a[i][i] - sum;
                    if (diag <= 1e-14) return false;
                    l[i][j] = Math.Sqrt(diag);
                }
                else l[i][j] = (a[i][j] - sum) / l[j][j];
            }
        }
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = b[i];
            for (int k = 0; k < i; k++) sum -= l[i][k] * y[k];
            y[i] = sum / l[i][i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = y[i];
            for (int k = i + 1; k < n; k++) sum -= l[k][i] * x[k];
            x[i] = sum / l[i][i];
        }
        return true;
    }
}

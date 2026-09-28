using System;

namespace IsotopeMixing.Domain;

public static class LA
{
    public static double[][] Mat(int rows, int cols)
    {
        var m = new double[rows][];
        for (int i = 0; i < rows; i++) m[i] = new double[cols];
        return m;
    }

    public static double[][] T(double[][] a)
    {
        int r = a.Length, c = a[0].Length;
        var t = Mat(c, r);
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                t[j][i] = a[i][j];
        return t;
    }

    public static double[][] Mul(double[][] a, double[][] b)
    {
        int r = a.Length, k = a[0].Length, c = b[0].Length;
        var m = Mat(r, c);
        for (int i = 0; i < r; i++)
            for (int p = 0; p < k; p++)
            {
                double aik = a[i][p];
                if (aik == 0) continue;
                for (int j = 0; j < c; j++) m[i][j] += aik * b[p][j];
            }
        return m;
    }

    public static double[] MulVec(double[][] a, double[] x)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            double s = 0;
            for (int j = 0; j < x.Length; j++) s += a[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double[] Solve(double[][] a, double[] b)
    {
        int n = a.Length;
        var aug = Mat(n, n + 1);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i][j] = a[i][j];
            aug[i][n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[piv][col])) piv = r;
            if (Math.Abs(aug[piv][col]) < 1e-14) throw new InvalidOperationException("Singular matrix");
            (aug[piv], aug[col]) = (aug[col], aug[piv]);
            double d = aug[col][col];
            for (int j = col; j <= n; j++) aug[col][j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = aug[r][col];
                if (f == 0) continue;
                for (int j = col; j <= n; j++) aug[r][j] -= f * aug[col][j];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = aug[i][n];
        return x;
    }

    public static bool TryCholesky(double[][] a, out double[][] lower)
    {
        int n = a.Length;
        lower = Mat(n, n);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double s = a[i][j];
                for (int k = 0; k < j; k++) s -= lower[i][k] * lower[j][k];
                if (i == j)
                {
                    if (s <= 1e-14) return false;
                    lower[i][j] = Math.Sqrt(s);
                }
                else lower[i][j] = s / lower[j][j];
            }
        }
        return true;
    }

    public static (double[] eig, double[][] vec) SymmetricEigen(double[][] a)
    {
        int n = a.Length;
        var (eig, v) = JacobiRaw(a);
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        Array.Sort(idx, (i, j) => eig[j].CompareTo(eig[i]));
        var ordered = Mat(n, n);
        var e2 = new double[n];
        for (int col = 0; col < n; col++)
        {
            e2[col] = eig[idx[col]];
            for (int row = 0; row < n; row++) ordered[row][col] = v[row][idx[col]];
        }
        return (e2, ordered);
    }

    private static (double[] eig, double[][] vec) JacobiRaw(double[][] aIn)
    {
        int n = aIn.Length;
        var a = Mat(n, n);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                a[i][j] = aIn[i][j];
        var v = Mat(n, n);
        for (int i = 0; i < n; i++) v[i][i] = 1;
        for (int sweep = 0; sweep < 100; sweep++)
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
                    if (Math.Abs(apq) < 1e-15) continue;
                    double app = a[p][p], aqq = a[q][q];
                    double phi = Math.Abs(app - aqq) < 1e-30
                        ? Math.PI / 4
                        : 0.5 * Math.Atan2(2 * apq, app - aqq);
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
        var eig = new double[n];
        for (int i = 0; i < n; i++) eig[i] = a[i][i];
        return (eig, v);
    }

    public static double[] SingularValuesDesc(double[][] matrix)
    {
        var ata = Mul(T(matrix), matrix);
        var (eig, _) = SymmetricEigen(ata);
        for (int i = 0; i < eig.Length; i++) eig[i] = Math.Sqrt(Math.Max(0, eig[i]));
        return eig;
    }
}

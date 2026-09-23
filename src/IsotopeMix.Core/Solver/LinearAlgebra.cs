using System;

namespace IsotopeMix.Core.Solver;

// 极小自包含线性代数：正定求逆与单边 Jacobi SVD（无需外部数值包）。
public static class LinearAlgebra
{
    // 对称正定矩阵求逆（伴随法，维度很小，2~4）。
    public static double[,] InvertSymmetric(double[,] a)
    {
        int n = a.GetLength(0);
        if (a.GetLength(1) != n) throw new ArgumentException("matrix must be square");
        double det = Determinant(a);
        if (Math.Abs(det) < 1e-14) throw new InvalidOperationException("matrix is singular");
        if (n == 1) return new double[1, 1] { { 1.0 / a[0, 0] } };
        double[,] inv = new double[n, n];
        double[,] adj = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double[,] minor = Minor(a, i, j);
                adj[j, i] = ((i + j) % 2 == 0 ? 1 : -1) * Determinant(minor);
            }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                inv[i, j] = adj[i, j] / det;
        return inv;
    }

    public static double Determinant(double[,] a)
    {
        int n = a.GetLength(0);
        if (n == 1) return a[0, 0];
        if (n == 2) return a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        double det = 0;
        for (int p = 0; p < n; p++)
        {
            double[,] m = Minor(a, 0, p);
            det += (p % 2 == 0 ? 1 : -1) * a[0, p] * Determinant(m);
        }
        return det;
    }

    static double[,] Minor(double[,] a, int excludeRow, int excludeCol)
    {
        int n = a.GetLength(0);
        double[,] m = new double[n - 1, n - 1];
        int r = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == excludeRow) continue;
            int c = 0;
            for (int j = 0; j < n; j++)
            {
                if (j == excludeCol) continue;
                m[r, c++] = a[i, j];
            }
            r++;
        }
        return m;
    }

    // 单边 Jacobi SVD：A = U S V^T。返回奇异值（降序），并通过 out 返回 V 列。
    public static void Svd(double[,] a, out double[] singularValues, out double[,] v)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        double[,] work = (double[,])a.Clone();
        v = Identity(cols);
        double tol = 1e-14;
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < cols - 1; p++)
                for (int q = p + 1; q < cols; q++)
                {
                    double alpha = 0, beta = 0, gamma = 0;
                    for (int i = 0; i < rows; i++)
                    {
                        alpha += work[i, p] * work[i, p];
                        beta += work[i, q] * work[i, q];
                        gamma += work[i, p] * work[i, q];
                    }
                    off += gamma * gamma;
                    if (Math.Abs(gamma) <= tol * Math.Sqrt(alpha * beta)) continue;
                    double zeta = (beta - alpha) / (2.0 * gamma);
                    double t = (zeta >= 0 ? 1 : -1) /
                               (Math.Abs(zeta) + Math.Sqrt(1.0 + zeta * zeta));
                    double cs = 1.0 / Math.Sqrt(1.0 + t * t);
                    double sn = t * cs;
                    for (int i = 0; i < rows; i++)
                    {
                        double x = work[i, p];
                        double y = work[i, q];
                        work[i, p] = cs * x - sn * y;
                        work[i, q] = sn * x + cs * y;
                    }
                    for (int i = 0; i < cols; i++)
                    {
                        double x = v[i, p];
                        double y = v[i, q];
                        v[i, p] = cs * x - sn * y;
                        v[i, q] = sn * x + cs * y;
                    }
                }
            if (off < tol) break;
        }
        singularValues = new double[cols];
        for (int j = 0; j < cols; j++)
        {
            double norm = 0;
            for (int i = 0; i < rows; i++) norm += work[i, j] * work[i, j];
            singularValues[j] = Math.Sqrt(norm);
        }
        Array.Sort(singularValues);
        Array.Reverse(singularValues);
    }

    static double[,] Identity(int n)
    {
        double[,] id = new double[n, n];
        for (int i = 0; i < n; i++) id[i, i] = 1;
        return id;
    }

    public static double[] SolveLinear2(double a11, double a12, double a21, double a22,
                                        double b1, double b2)
    {
        double det = a11 * a22 - a12 * a21;
        return new[]
        {
            (b1 * a22 - a12 * b2) / det,
            (a11 * b2 - b1 * a21) / det
        };
    }
}

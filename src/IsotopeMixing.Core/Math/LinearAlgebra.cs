using System;

namespace IsotopeMixing.Core.Math;

public static class LinearAlgebra
{
    // 对称正定矩阵解 Ax=b（Cholesky）。相关误差的协方差传播走这里，
    // 而不是把非对角元素扔掉当独立误差。
    public static double[] SolvePositiveDefinite(double[][] a, double[] b)
    {
        int n = a.Length;
        var l = new double[n][];
        for (int i = 0; i < n; i++) l[i] = new double[n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i][j];
                for (int k = 0; k < j; k++) sum -= l[i][k] * l[j][k];
                if (i == j)
                {
                    if (sum <= 1e-14)
                        throw new InvalidOperationException("协方差矩阵非正定，无法传播相关误差。");
                    l[i][j] = System.Math.Sqrt(sum);
                }
                else
                {
                    l[i][j] = sum / l[j][j];
                }
            }
        }

        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = b[i];
            for (int k = 0; k < i; k++) sum -= l[i][k] * y[k];
            y[i] = sum / l[i][i];
        }

        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = y[i];
            for (int k = i + 1; k < n; k++) sum -= l[k][i] * x[k];
            x[i] = sum / l[i][i];
        }
        return x;
    }

    public static double QuadraticForm(double[][] a, double[] v)
    {
        double[] solved = SolvePositiveDefinite(a, v);
        double sum = 0;
        for (int i = 0; i < v.Length; i++) sum += v[i] * solved[i];
        return sum;
    }

    // Jacobi 特征值（实对称矩阵，维度很小）。用于求 A^T A 的奇异值，
    // 判断端元在同位素空间中是否近共线。
    public static double[] SymmetricEigenvalues(double[][] matrix)
    {
        int n = matrix.Length;
        var a = new double[n][];
        for (int i = 0; i < n; i++)
        {
            a[i] = new double[n];
            Array.Copy(matrix[i], a[i], n);
        }

        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p][q] * a[p][q];
            if (off < 1e-20) break;

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    if (System.Math.Abs(a[p][q]) < 1e-15) continue;
                    double tau = (a[q][q] - a[p][p]) / (2 * a[p][q]);
                    double t = System.Math.Sign(tau) / (System.Math.Abs(tau) + System.Math.Sqrt(1 + tau * tau));
                    if (tau == 0) t = 1;
                    double c = 1 / System.Math.Sqrt(1 + t * t);
                    double s = t * c;

                    double app = a[p][p];
                    a[p][p] = c * c * app - 2 * s * c * a[p][q] + s * s * a[q][q];
                    a[q][q] = s * s * app + 2 * s * c * a[p][q] + c * c * a[q][q];
                    a[p][q] = 0;
                    a[q][p] = 0;
                    for (int r = 0; r < n; r++)
                    {
                        if (r == p || r == q) continue;
                        double arp = a[r][p];
                        double arq = a[r][q];
                        a[r][p] = c * arp - s * arq;
                        a[p][r] = a[r][p];
                        a[r][q] = s * arp + c * arq;
                        a[q][r] = a[r][q];
                    }
                }
            }
        }

        var values = new double[n];
        for (int i = 0; i < n; i++) values[i] = a[i][i];
        Array.Sort(values);
        Array.Reverse(values);
        return values;
    }

    // 设计矩阵（k 行 × m 列，已中心化）的奇异值。
    public static double[] SingularValues(double[][] design)
    {
        int rows = design.Length;
        int cols = design[0].Length;
        var ata = new double[cols][];
        for (int i = 0; i < cols; i++)
        {
            ata[i] = new double[cols];
            for (int j = 0; j < cols; j++)
                for (int r = 0; r < rows; r++)
                    ata[i][j] += design[r][i] * design[r][j];
        }
        var eigs = SymmetricEigenvalues(ata);
        for (int i = 0; i < eigs.Length; i++) eigs[i] = System.Math.Sqrt(System.Math.Max(0, eigs[i]));
        return eigs;
    }
}

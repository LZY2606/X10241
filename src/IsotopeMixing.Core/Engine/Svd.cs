using System;

namespace IsotopeMixing.Core.Engine;

/// <summary>薄 SVD：A(n x m) = U S Vt。通过 A^T A 的特征分解得到奇异值与 V，再推 U。</summary>
public sealed class ThinSvd
{
    public required double[] SingularValues { get; init; }
    public required double[,] U { get; init; }
    public required double[,] V { get; init; }

    public static ThinSvd Decompose(double[,] a)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var ata = new double[m, m];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int k = 0; k < n; k++) s += a[k, i] * a[k, j];
                ata[i, j] = s;
            }
        var (eig, v) = LinearAlgebra.SymmetricEigen(ata);
        int r = Math.Min(n, m);
        var sv = new double[r];
        var u = new double[n, r];
        for (int j = 0; j < r; j++)
        {
            double s = Math.Sqrt(Math.Max(eig[j], 0));
            sv[j] = s;
            if (s > 1e-12)
                for (int i = 0; i < n; i++)
                {
                    double dot = 0;
                    for (int t = 0; t < m; t++) dot += a[i, t] * v[t, j];
                    u[i, j] = dot / s;
                }
            else
                for (int i = 0; i < n; i++) u[i, j] = i == j ? 1.0 : 0.0;
        }
        // 特征值升序 -> 需要降序 SVD
        Array.Reverse(sv);
        var ur = new double[n, r];
        var vr = new double[m, r];
        for (int j = 0; j < r; j++)
        {
            int src = r - 1 - j;
            for (int i = 0; i < n; i++) ur[i, j] = u[i, src];
            for (int i = 0; i < m; i++) vr[i, j] = v[i, src];
        }
        return new ThinSvd { SingularValues = sv, U = ur, V = vr };
    }
}

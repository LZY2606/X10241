namespace MixingStudio.Math;

/// <summary>Small dense linear algebra helpers (no external deps).</summary>
public static class LinearAlgebra
{
    public static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (System.Math.Abs(m[r, col]) > System.Math.Abs(m[piv, col])) piv = r;
            if (System.Math.Abs(m[piv, col]) < 1e-14)
                throw new InvalidOperationException("Singular matrix");
            if (piv != col)
            {
                for (int c = 0; c < n; c++) (m[col, c], m[piv, c]) = (m[piv, c], m[col, c]);
                (x[col], x[piv]) = (x[piv], x[col]);
            }
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int c = col; c < n; c++) m[r, c] -= f * m[col, c];
                x[r] -= f * x[col];
            }
        }
        for (int col = n - 1; col >= 0; col--)
        {
            for (int r = col + 1; r < n; r++) x[col] -= m[col, r] * x[r];
            x[col] /= m[col, col];
        }
        return x;
    }

    public static double[,] Invert(double[,] a)
    {
        int n = a.GetLength(0);
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            var e = new double[n];
            e[i] = 1.0;
            var col = Solve(a, e);
            for (int r = 0; r < n; r++) inv[r, i] = col[r];
        }
        return inv;
    }

    /// <summary>Eigenvalues of a symmetric matrix via cyclic Jacobi rotations.</summary>
    public static double[] SymmetricEigenvalues(double[,] a)
    {
        int n = a.GetLength(0);
        var m = (double[,])a.Clone();
        for (int sweep = 0; sweep < 64; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p, q] * m[p, q];
            if (off < 1e-20) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (System.Math.Abs(m[p, q]) < 1e-15) continue;
                    double theta = 0.5 * System.Math.Atan2(2 * m[p, q], m[q, q] - m[p, p]);
                    double c = System.Math.Cos(theta), s = System.Math.Sin(theta);
                    for (int k = 0; k < n; k++)
                    {
                        double mkp = m[k, p], mkq = m[k, q];
                        m[k, p] = c * mkp - s * mkq;
                        m[k, q] = s * mkp + c * mkq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double mpk = m[p, k], mqk = m[q, k];
                        m[p, k] = c * mpk - s * mqk;
                        m[q, k] = s * mpk + c * mqk;
                    }
                }
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = m[i, i];
        Array.Sort(vals);
        return vals;
    }

    /// <summary>Singular values of a (rows x cols) matrix, ascending.</summary>
    public static double[] SingularValues(double[,] a)
    {
        int rows = a.GetLength(0), cols = a.GetLength(1);
        var ata = new double[cols, cols];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
            {
                double s = 0;
                for (int r = 0; r < rows; r++) s += a[r, i] * a[r, j];
                ata[i, j] = s;
            }
        var eig = SymmetricEigenvalues(ata);
        for (int i = 0; i < eig.Length; i++) eig[i] = System.Math.Sqrt(System.Math.Max(0, eig[i]));
        return eig;
    }

    public static double Mahalanobis2(double[] residual, double[,] covariance)
    {
        var solved = Solve(covariance, residual);
        double d2 = 0;
        for (int i = 0; i < residual.Length; i++) d2 += residual[i] * solved[i];
        return d2;
    }
}

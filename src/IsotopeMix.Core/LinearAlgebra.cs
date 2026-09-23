namespace IsotopeMix.Core;

/// <summary>Small dense-matrix helpers (Jacobi eigenvalues, inverse, solve).</summary>
public static class LinearAlgebra
{
    public static double[,] Identity(int n)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1.0;
        return m;
    }

    public static double[,] Transpose(double[,] a)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var t = new double[c, r];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) t[j, i] = a[i, j];
        return t;
    }

    public static double[,] Multiply(double[,] a, double[,] b)
    {
        int r = a.GetLength(0), k = a.GetLength(1), c = b.GetLength(1);
        var m = new double[r, c];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
            {
                double s = 0;
                for (int t = 0; t < k; t++) s += a[i, t] * b[t, j];
                m[i, j] = s;
            }
        return m;
    }

    public static double[] Multiply(double[,] a, double[] x)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var y = new double[r];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) y[i] += a[i, j] * x[j];
        return y;
    }

    /// <summary>Gauss-Jordan inverse with partial pivoting. Throws if singular.</summary>
    public static double[,] Invert(double[,] a)
    {
        int n = a.GetLength(0);
        var aug = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) aug[i, j] = a[i, j];
            aug[i, n + i] = 1.0;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(aug[r, col]) > Math.Abs(aug[piv, col])) piv = r;
            if (Math.Abs(aug[piv, col]) < 1e-14)
                throw new InvalidOperationException("matrix is singular");
            for (int j = 0; j < 2 * n; j++) (aug[col, j], aug[piv, j]) = (aug[piv, j], aug[col, j]);
            double d = aug[col, col];
            for (int j = 0; j < 2 * n; j++) aug[col, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = aug[r, col];
                for (int j = 0; j < 2 * n; j++) aug[r, j] -= f * aug[col, j];
            }
        }
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) inv[i, j] = aug[i, n + j];
        return inv;
    }

    /// <summary>Jacobi eigenvalue iteration for a symmetric matrix. Returns eigenvalues ascending.</summary>
    public static double[] SymmetricEigenvalues(double[,] a)
    {
        int n = a.GetLength(0);
        var m = (double[,])a.Clone();
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++) off += m[p, q] * m[p, q];
            if (off < 1e-24) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    double apq = m[p, q];
                    if (Math.Abs(apq) < 1e-300) continue;
                    double app = m[p, p], aqq = m[q, q];
                    double theta = 0.5 * Math.Atan2(2 * apq, aqq - app);
                    double c = Math.Cos(theta), s = Math.Sin(theta);
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

    /// <summary>Singular values of a (tall or square) matrix via eigenvalues of AᵀA.</summary>
    public static double[] SingularValues(double[,] a)
    {
        var ata = Multiply(Transpose(a), a);
        var ev = SymmetricEigenvalues(ata);
        for (int i = 0; i < ev.Length; i++) ev[i] = Math.Sqrt(Math.Max(0, ev[i]));
        return ev;
    }
}

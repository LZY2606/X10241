namespace IsotopeMix.Math;

/// <summary>Minimal dense linear algebra: solve, inverse, symmetric eigen (Jacobi), SVD via A^T A.</summary>
public static class LinAlg
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
                throw new InvalidOperationException("Singular matrix in solve.");
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
        var res = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = x[r];
            for (int c = r + 1; c < n; c++) s -= m[r, c] * res[c];
            res[r] = s / m[r, r];
        }
        return res;
    }

    public static double[,] Inverse(double[,] a)
    {
        int n = a.GetLength(0);
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            var e = new double[n]; e[i] = 1.0;
            var col = Solve(a, e);
            for (int r = 0; r < n; r++) inv[r, i] = col[r];
        }
        return inv;
    }

    /// <summary>Jacobi eigenvalue for symmetric matrices. Returns eigenvalues ascending and eigenvectors as columns.</summary>
    public static (double[] values, double[,] vectors) EigenSymmetric(double[,] a)
    {
        int n = a.GetLength(0);
        var m = (double[,])a.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++) for (int q = p + 1; q < n; q++) off += m[p, q] * m[p, q];
            if (off < 1e-24) break;
            for (int p = 0; p < n; p++) for (int q = p + 1; q < n; q++)
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
                for (int k = 0; k < n; k++)
                {
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = m[i, i];
        var order = Enumerable.Range(0, n).OrderBy(i => vals[i]).ToArray();
        var sv = new double[n];
        var vv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            sv[i] = vals[order[i]];
            for (int r = 0; r < n; r++) vv[r, i] = v[r, order[i]];
        }
        return (sv, vv);
    }

    /// <summary>Singular values of a (possibly rectangular) matrix, ascending.</summary>
    public static double[] SingularValues(double[,] a)
    {
        int rows = a.GetLength(0), cols = a.GetLength(1);
        var ata = new double[cols, cols];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
            {
                double s = 0;
                for (int k = 0; k < rows; k++) s += a[k, i] * a[k, j];
                ata[i, j] = s;
            }
        var (vals, _) = EigenSymmetric(ata);
        return vals.Select(v => System.Math.Sqrt(System.Math.Max(0, v))).ToArray();
    }
}

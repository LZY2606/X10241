namespace IsoMix.Math;

/// <summary>Small dense-matrix helpers for the mixing-model math (sizes are tiny: dims &le; 4, endmembers &le; 6).</summary>
public static class LinAlg
{
    public static double[,] Transpose(double[,] a)
    {
        var r = new double[a.GetLength(1), a.GetLength(0)];
        for (var i = 0; i < a.GetLength(0); i++)
        for (var j = 0; j < a.GetLength(1); j++)
            r[j, i] = a[i, j];
        return r;
    }

    public static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[a.GetLength(0), b.GetLength(1)];
        for (var i = 0; i < a.GetLength(0); i++)
        for (var j = 0; j < b.GetLength(1); j++)
        for (var k = 0; k < a.GetLength(1); k++)
            r[i, j] += a[i, k] * b[k, j];
        return r;
    }

    public static double[] MulVec(double[,] a, double[] x)
    {
        var r = new double[a.GetLength(0)];
        for (var i = 0; i < a.GetLength(0); i++)
        for (var k = 0; k < a.GetLength(1); k++)
            r[i] += a[i, k] * x[k];
        return r;
    }

    /// <summary>Gauss-Jordan inverse with partial pivoting. Throws when singular.</summary>
    public static double[,] Inverse(double[,] a)
    {
        var n = a.GetLength(0);
        var aug = new double[n, 2 * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) aug[i, j] = a[i, j];
            aug[i, n + i] = 1.0;
        }
        for (var col = 0; col < n; col++)
        {
            var piv = col;
            for (var r = col + 1; r < n; r++)
                if (System.Math.Abs(aug[r, col]) > System.Math.Abs(aug[piv, col])) piv = r;
            if (System.Math.Abs(aug[piv, col]) < 1e-14) throw new InvalidOperationException("singular matrix");
            if (piv != col)
                for (var j = 0; j < 2 * n; j++) (aug[col, j], aug[piv, j]) = (aug[piv, j], aug[col, j]);
            var d = aug[col, col];
            for (var j = 0; j < 2 * n; j++) aug[col, j] /= d;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = aug[r, col];
                if (f == 0) continue;
                for (var j = 0; j < 2 * n; j++) aug[r, j] -= f * aug[col, j];
            }
        }
        var inv = new double[n, n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            inv[i, j] = aug[i, n + j];
        return inv;
    }

    /// <summary>Jacobi eigen decomposition of a symmetric matrix. Returns eigenvalues ascending and eigenvectors as columns.</summary>
    public static (double[] Values, double[,] Vectors) EigenSymmetric(double[,] a)
    {
        var n = a.GetLength(0);
        var m = (double[,])a.Clone();
        var v = new double[n, n];
        for (var i = 0; i < n; i++) v[i, i] = 1.0;
        for (var sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
                off += m[p, q] * m[p, q];
            if (off < 1e-20) break;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (System.Math.Abs(m[p, q]) < 1e-15) continue;
                var theta = 0.5 * System.Math.Atan2(2 * m[p, q], m[q, q] - m[p, p]);
                var c = System.Math.Cos(theta);
                var s = System.Math.Sin(theta);
                for (var i = 0; i < n; i++)
                {
                    (m[i, p], m[i, q]) = (c * m[i, p] - s * m[i, q], s * m[i, p] + c * m[i, q]);
                    (v[i, p], v[i, q]) = (c * v[i, p] - s * v[i, q], s * v[i, p] + c * v[i, q]);
                }
                for (var i = 0; i < n; i++)
                    (m[p, i], m[q, i]) = (c * m[p, i] - s * m[q, i], s * m[p, i] + c * m[q, i]);
            }
        }
        var vals = new double[n];
        for (var i = 0; i < n; i++) vals[i] = m[i, i];
        // sort ascending, keep eigenvector columns aligned
        var order = vals.Select((x, i) => (x, i)).OrderBy(t => t.x).Select(t => t.i).ToArray();
        var sortedVals = order.Select(i => vals[i]).ToArray();
        var sortedVec = new double[n, n];
        for (var col = 0; col < n; col++)
        for (var i = 0; i < n; i++)
            sortedVec[i, col] = v[i, order[col]];
        return (sortedVals, sortedVec);
    }

    /// <summary>Ordinary least squares via normal equations: min ||E p - s||.</summary>
    public static double[] LeastSquares(double[,] e, double[] s)
    {
        var et = Transpose(e);
        var ete = Mul(et, e);
        var ets = MulVec(et, s);
        // Tikhonov nudge keeps near-collinear systems solvable; the caller inspects identifiability separately.
        for (var i = 0; i < ete.GetLength(0); i++) ete[i, i] += 1e-10;
        var inv = Inverse(ete);
        return MulVec(inv, ets);
    }

    /// <summary>
    /// Euclidean projection onto the probability simplex {p : sum p = 1, p &ge; 0}.
    /// This is the only allowed way to force simplex constraints; clip-then-renormalize is never used.
    /// </summary>
    public static double[] ProjectToSimplex(double[] v)
    {
        var u = v.OrderByDescending(x => x).ToArray();
        double css = 0;
        var rho = -1;
        for (var j = 0; j < u.Length; j++)
        {
            css += u[j];
            if (u[j] + (1.0 - css) / (j + 1) > 0) rho = j;
        }
        var theta = (u.Take(rho + 1).Sum() - 1.0) / (rho + 1);
        return v.Select(x => System.Math.Max(x - theta, 0.0)).ToArray();
    }

    /// <summary>Mahalanobis distance r' &Sigma;^-1 r using the full (possibly correlated) covariance.</summary>
    public static double Mahalanobis(double[] r, double[,] cov)
    {
        var inv = Inverse(cov);
        var t = MulVec(inv, r);
        double m = 0;
        for (var i = 0; i < r.Length; i++) m += r[i] * t[i];
        return m;
    }

    public static double Determinant(double[,] a)
    {
        var n = a.GetLength(0);
        var m = (double[,])a.Clone();
        double det = 1;
        for (var col = 0; col < n; col++)
        {
            var piv = col;
            for (var r = col + 1; r < n; r++)
                if (System.Math.Abs(m[r, col]) > System.Math.Abs(m[piv, col])) piv = r;
            if (System.Math.Abs(m[piv, col]) < 1e-300) return 0;
            if (piv != col)
            {
                for (var j = 0; j < n; j++) (m[col, j], m[piv, j]) = (m[piv, j], m[col, j]);
                det = -det;
            }
            det *= m[col, col];
            for (var r = col + 1; r < n; r++)
            {
                var f = m[r, col] / m[col, col];
                for (var j = col; j < n; j++) m[r, j] -= f * m[col, j];
            }
        }
        return det;
    }
}

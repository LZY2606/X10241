namespace IsoMix.Web.Domain;

/// <summary>Minimal linear algebra for small dense problems (isotope dims below 8).</summary>
public static class LinAlg
{
    public static double[][] Invert(double[][] a)
    {
        int n = a.Length;
        var m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[2 * n];
            for (int j = 0; j < n; j++) m[i][j] = a[i][j];
            m[i][n + i] = 1.0;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r][col]) > Math.Abs(m[piv][col])) piv = r;
            if (Math.Abs(m[piv][col]) < 1e-15)
                throw new InvalidOperationException("Matrix is singular; covariance must be positive definite.");
            (m[col], m[piv]) = (m[piv], m[col]);
            double d = m[col][col];
            for (int j = 0; j < 2 * n; j++) m[col][j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r][col];
                if (f == 0) continue;
                for (int j = 0; j < 2 * n; j++) m[r][j] -= f * m[col][j];
            }
        }
        var inv = new double[n][];
        for (int i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            for (int j = 0; j < n; j++) inv[i][j] = m[i][n + j];
        }
        return inv;
    }

    public static double Mahalanobis2(double[] r, double[][] sigmaInv)
    {
        int n = r.Length;
        double d2 = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                d2 += r[i] * sigmaInv[i][j] * r[j];
        return d2;
    }

    /// <summary>Jacobi eigenvalue iteration for a symmetric matrix. Returns eigenvalues ascending.</summary>
    public static double[] EigenvaluesSymmetric(double[][] a)
    {
        int n = a.Length;
        var m = a.Select(row => (double[])row.Clone()).ToArray();
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p][q] * m[p][q];
            if (off < 1e-24) break;
            for (int p = 0; p < n; p++)
            for (int q = p + 1; q < n; q++)
            {
                if (Math.Abs(m[p][q]) < 1e-15) continue;
                double theta = 0.5 * Math.Atan2(2 * m[p][q], m[q][q] - m[p][p]);
                double c = Math.Cos(theta), s = Math.Sin(theta);
                for (int k = 0; k < n; k++)
                {
                    double mkp = m[k][p], mkq = m[k][q];
                    m[k][p] = c * mkp - s * mkq;
                    m[k][q] = s * mkp + c * mkq;
                }
                for (int k = 0; k < n; k++)
                {
                    double mpk = m[p][k], mqk = m[q][k];
                    m[p][k] = c * mpk - s * mqk;
                    m[q][k] = s * mpk + c * mqk;
                }
            }
        }
        return Enumerable.Range(0, n).Select(i => m[i][i]).OrderBy(v => v).ToArray();
    }

    /// <summary>Andrew monotone chain convex hull for 2D points. Returns hull vertices CCW.</summary>
    public static (double x, double y)[] ConvexHull2D(IReadOnlyList<(double x, double y)> pts)
    {
        var sorted = pts.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToArray();
        if (sorted.Length <= 2) return sorted;
        static double Cross((double x, double y) o, (double x, double y) a, (double x, double y) b)
            => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var lower = new List<(double x, double y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double x, double y)>();
        foreach (var p in sorted.Reverse())
        {
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return lower.Concat(upper).ToArray();
    }

    /// <summary>Chi-square 0.95 quantiles for small degrees of freedom.</summary>
    public static double ChiSquare95(int df) => df switch
    {
        1 => 3.841, 2 => 5.991, 3 => 7.815, 4 => 9.488,
        5 => 11.070, 6 => 12.592, 7 => 14.067, 8 => 15.507,
        _ => df + 1.645 * Math.Sqrt(2.0 * df)
    };
}

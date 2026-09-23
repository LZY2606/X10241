namespace IsoMix.Domain;

public static class LinAlg
{
    public static double[][] Identity(int n)
    {
        var m = new double[n][];
        for (int i = 0; i < n; i++) { m[i] = new double[n]; m[i][i] = 1.0; }
        return m;
    }

    public static double[][] Transpose(double[][] a)
    {
        int r = a.Length, c = a[0].Length;
        var t = new double[c][];
        for (int j = 0; j < c; j++) { t[j] = new double[r]; for (int i = 0; i < r; i++) t[j][i] = a[i][j]; }
        return t;
    }

    public static double[][] Mul(double[][] a, double[][] b)
    {
        int r = a.Length, k = b.Length, c = b[0].Length;
        var m = new double[r][];
        for (int i = 0; i < r; i++)
        {
            m[i] = new double[c];
            for (int j = 0; j < c; j++)
            {
                double s = 0;
                for (int l = 0; l < k; l++) s += a[i][l] * b[l][j];
                m[i][j] = s;
            }
        }
        return m;
    }

    public static double[] Mul(double[][] a, double[] x)
    {
        int r = a.Length, k = x.Length;
        var y = new double[r];
        for (int i = 0; i < r; i++)
        {
            double s = 0;
            for (int l = 0; l < k; l++) s += a[i][l] * x[l];
            y[i] = s;
        }
        return y;
    }

    public static double[] Add(double[] a, double[] b)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++) y[i] = a[i] + b[i];
        return y;
    }

    public static double[] Sub(double[] a, double[] b)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++) y[i] = a[i] - b[i];
        return y;
    }

    public static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static double[][] Inverse(double[][] a)
    {
        int n = a.Length;
        var aug = new double[n][];
        for (int i = 0; i < n; i++)
        {
            aug[i] = new double[2 * n];
            for (int j = 0; j < n; j++) aug[i][j] = a[i][j];
            aug[i][n + i] = 1.0;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[piv][col])) piv = r;
            if (Math.Abs(aug[piv][col]) < 1e-300) throw new InvalidOperationException("matrix is singular");
            (aug[col], aug[piv]) = (aug[piv], aug[col]);
            double d = aug[col][col];
            for (int j = 0; j < 2 * n; j++) aug[col][j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = aug[r][col];
                if (f == 0) continue;
                for (int j = 0; j < 2 * n; j++) aug[r][j] -= f * aug[col][j];
            }
        }
        var inv = new double[n][];
        for (int i = 0; i < n; i++) { inv[i] = new double[n]; Array.Copy(aug[i], n, inv[i], 0, n); }
        return inv;
    }

    public static bool TrySolve(double[][] a, double[] b, out double[] x)
    {
        int n = a.Length;
        var aug = new double[n][];
        for (int i = 0; i < n; i++)
        {
            aug[i] = new double[n + 1];
            for (int j = 0; j < n; j++) aug[i][j] = a[i][j];
            aug[i][n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[piv][col])) piv = r;
            if (Math.Abs(aug[piv][col]) < 1e-12) { x = Array.Empty<double>(); return false; }
            (aug[col], aug[piv]) = (aug[piv], aug[col]);
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
        x = new double[n];
        for (int i = 0; i < n; i++) x[i] = aug[i][n];
        return true;
    }

    // Cyclic Jacobi eigenvalue decomposition for symmetric matrices.
    public static (double[] Values, double[][] Vectors) EigenSymmetric(double[][] a)
    {
        int n = a.Length;
        var m = a.Select(row => (double[])row.Clone()).ToArray();
        var v = Identity(n);
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p][q] * m[p][q];
            if (off < 1e-30) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(m[p][q]) < 1e-300) continue;
                    double theta = 0.5 * (m[q][q] - m[p][p]) / m[p][q];
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1.0;
                    double c = 1.0 / Math.Sqrt(t * t + 1);
                    double s = t * c;
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
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k][p], vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
        }
        var values = new double[n];
        for (int i = 0; i < n; i++) values[i] = m[i][i];
        var order = Enumerable.Range(0, n).OrderByDescending(i => values[i]).ToArray();
        var sortedValues = order.Select(i => values[i]).ToArray();
        var sortedVectors = new double[n][];
        for (int k = 0; k < n; k++)
        {
            sortedVectors[k] = new double[n];
            for (int r = 0; r < n; r++) sortedVectors[k][r] = v[r][order[k]];
        }
        return (sortedValues, sortedVectors);
    }

    // Singular values of a (rows x cols) via eigen of A^T A.
    public static (double[] SingularValues, double ConditionNumber, int Rank) Analyze(double[][] a, double rankTolerance)
    {
        var ata = Mul(Transpose(a), a);
        var (values, _) = EigenSymmetric(ata);
        var sv = values.Select(v => Math.Sqrt(Math.Max(v, 0))).ToArray();
        double max = sv.Length > 0 ? sv[0] : 0;
        double tol = max * Math.Max(a.Length, a[0].Length) * rankTolerance;
        int rank = sv.Count(s => s > tol);
        double min = sv.Length > 0 ? sv[^1] : 0;
        double cond = min > 0 ? max / min : double.PositiveInfinity;
        return (sv, cond, rank);
    }
}

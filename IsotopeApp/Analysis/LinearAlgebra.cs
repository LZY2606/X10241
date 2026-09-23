namespace IsotopeApp.Analysis;

/// <summary>Small dense linear-algebra helpers used by the mixing solver.</summary>
public static class Lin
{
    public static double[,] Identity(int n)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1;
        return m;
    }

    public static double[,] Add(double[,] a, double[,] b, double sb = 1.0)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var r = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                r[i, j] = a[i, j] + sb * b[i, j];
        return r;
    }

    public static double[,] Scale(double[,] a, double s)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var r = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                r[i, j] = a[i, j] * s;
        return r;
    }

    public static double[,] Multiply(double[,] a, double[,] b)
    {
        int n = a.GetLength(0), k = a.GetLength(1), m = b.GetLength(1);
        var r = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int t = 0; t < k; t++)
            {
                double va = a[i, t];
                if (va == 0) continue;
                for (int j = 0; j < m; j++) r[i, j] += va * b[t, j];
            }
        return r;
    }

    public static double[] Multiply(double[,] a, double[] v)
    {
        int n = a.GetLength(0), k = a.GetLength(1);
        var r = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int j = 0; j < k; j++) s += a[i, j] * v[j];
            r[i] = s;
        }
        return r;
    }

    public static double[,] Transpose(double[,] a)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var r = new double[m, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                r[j, i] = a[i, j];
        return r;
    }

    public static double[,] Invert(double[,] a)
    {
        int n = a.GetLength(0);
        var m = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n + i] = 1;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
            for (int j = 0; j < 2 * n; j++)
                (m[col, j], m[piv, j]) = (m[piv, j], m[col, j]);
            double d = m[col, col];
            if (Math.Abs(d) < 1e-15) throw new InvalidOperationException("Singular matrix");
            for (int j = 0; j < 2 * n; j++) m[col, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r, col];
                if (f == 0) continue;
                for (int j = 0; j < 2 * n; j++) m[r, j] -= f * m[col, j];
            }
        }
        var res = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                res[i, j] = m[i, n + j];
        return res;
    }

    /// <summary>Symmetric SVD via Jacobi eigen-decomposition of A^T A.</summary>
    public static (double[] singularValues, double[,] u, double[,] v) Svd(double[,] a)
    {
        int m = a.GetLength(0), n = a.GetLength(1);
        var ata = Multiply(Transpose(a), a);
        var (evals, v) = JacobiEigen(ata);
        var sv = evals.Select(e => Math.Sqrt(Math.Max(0, e))).ToArray();
        var u = new double[m, Math.Min(m, n)];
        for (int k = 0; k < Math.Min(m, n); k++)
        {
            if (sv[k] > 1e-12)
            {
                var col = new double[n];
                for (int i = 0; i < n; i++) col[i] = v[i, k];
                var av = Multiply(a, col);
                for (int i = 0; i < m; i++) u[i, k] = av[i] / sv[k];
            }
        }
        return (sv, u, v);
    }

    public static (double[] values, double[,] vectors) JacobiEigen(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        var v = Identity(n);
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p, q] * a[p, q];
            if (off < 1e-24) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-15) continue;
                    double tau = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(tau == 0 ? 1 : tau) /
                               (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1 / Math.Sqrt(1 + t * t), s = t * c;
                    double app = a[p, p], aqq = a[q, q], apq = a[p, q];
                    a[p, p] = app - t * apq;
                    a[q, q] = aqq + t * apq;
                    a[p, q] = a[q, p] = 0;
                    for (int k = 0; k < n; k++)
                    {
                        if (k != p && k != q)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = a[p, k] = c * akp - s * akq;
                            a[k, q] = a[q, k] = s * akp + c * akq;
                        }
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
        }
        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = a[i, i];
        Array.Sort(vals, Enumerable.Range(0, n).ToArray());
        Array.Reverse(vals);
        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => a[i, i]).ToArray();
        var vecs = new double[n, n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                vecs[i, j] = v[i, order[j]];
        return (vals, vecs);
    }
}

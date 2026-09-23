namespace IsotopeMix.Core;

public static class MathUtil
{
    public static double[] Cholesky(IReadOnlyList<double> cov, int n)
    {
        var l = new double[n * n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j <= i; j++)
        {
            var sum = cov[i * n + j];
            for (var k = 0; k < j; k++) sum -= l[i * n + k] * l[j * n + k];
            if (i == j)
            {
                if (sum <= 1e-18) throw new InvalidOperationException("Covariance is not positive definite.");
                l[i * n + j] = Math.Sqrt(sum);
            }
            else l[i * n + j] = sum / l[j * n + j];
        }
        return l;
    }

    /// <summary>Solve L L' x = b given Cholesky factor L (row-major, n x n).</summary>
    public static double[] CholeskySolve(double[] l, double[] b, int n)
    {
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var k = 0; k < i; k++) sum -= l[i * n + k] * y[k];
            y[i] = sum / l[i * n + i];
        }
        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (var k = i + 1; k < n; k++) sum -= l[k * n + i] * x[k];
            x[i] = sum / l[i * n + i];
        }
        return x;
    }

    /// <summary>Condition number (2-norm) of a small dense matrix via power iteration on A'A and its inverse.</summary>
    public static double ConditionNumber(IReadOnlyList<double> a, int rows, int cols)
    {
        // Build normal equations M = A'A (cols x cols)
        var m = new double[cols * cols];
        for (var i = 0; i < cols; i++)
        for (var j = 0; j < cols; j++)
        {
            double sum = 0;
            for (var r = 0; r < rows; r++) sum += a[r * cols + i] * a[r * cols + j];
            m[i * cols + j] = sum;
        }
        var smax = Math.Sqrt(LargestEigen(m, cols));
        var smin = Math.Sqrt(SmallestEigen(m, cols));
        if (smin < 1e-15) return double.PositiveInfinity;
        return smax / smin;
    }

    private static double LargestEigen(double[] m, int n)
    {
        var v = Enumerable.Repeat(1.0 / Math.Sqrt(n), n).ToArray();
        for (var it = 0; it < 200; it++)
        {
            var w = Mul(m, v, n);
            var norm = Norm(w);
            if (norm < 1e-300) return 0;
            for (var i = 0; i < n; i++) v[i] = w[i] / norm;
        }
        var mv = Mul(m, v, n);
        return Dot(v, mv);
    }

    private static double SmallestEigen(double[] m, int n)
    {
        // inverse power iteration with LDL-free Gaussian elimination each step (n is tiny)
        var v = Enumerable.Repeat(1.0 / Math.Sqrt(n), n).ToArray();
        for (var it = 0; it < 300; it++)
        {
            double[] w;
            try { w = Solve(m, v, n); }
            catch { return 0; }
            var norm = Norm(w);
            if (norm < 1e-300 || double.IsInfinity(norm) || double.IsNaN(norm)) return 0;
            for (var i = 0; i < n; i++) v[i] = w[i] / norm;
        }
        try
        {
            var w2 = Solve(m, v, n);
            return 1.0 / Dot(v, w2);
        }
        catch { return 0; }
    }

    public static double[] Solve(IReadOnlyList<double> a, double[] b, int n)
    {
        var aug = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) aug[i, j] = a[i * n + j];
            aug[i, n] = b[i];
        }
        for (var col = 0; col < n; col++)
        {
            var piv = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(aug[r, col]) > Math.Abs(aug[piv, col])) piv = r;
            if (Math.Abs(aug[piv, col]) < 1e-300) throw new InvalidOperationException("Singular matrix.");
            if (piv != col)
                for (var j = 0; j <= n; j++) (aug[col, j], aug[piv, j]) = (aug[piv, j], aug[col, j]);
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = aug[r, col] / aug[col, col];
                for (var j = col; j <= n; j++) aug[r, j] -= f * aug[col, j];
            }
        }
        var x = new double[n];
        for (var i = 0; i < n; i++) x[i] = aug[i, n] / aug[i, i];
        return x;
    }

    private static double[] Mul(double[] m, double[] v, int n)
    {
        var r = new double[n];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            r[i] += m[i * n + j] * v[j];
        return r;
    }

    private static double Norm(double[] v) => Math.Sqrt(Dot(v, v));
    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    /// <summary>Chi-square 0.95 quantiles for small degrees of freedom.</summary>
    public static double ChiSquare95(int df) => df switch
    {
        1 => 3.8414588,
        2 => 5.9914645,
        3 => 7.8147279,
        4 => 9.4877290,
        5 => 11.0704977,
        6 => 12.5915872,
        _ => df + 2.3263479 * Math.Sqrt(2.0 * df) // Wilson-Hilferty-ish fallback
    };
}

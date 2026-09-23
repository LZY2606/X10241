namespace IsotopeMix.App.Domain;

public static class LinAlg
{
    public static double[] MatVec(double[,] a, double[] x)
    {
        var n = a.GetLength(0);
        var m = a.GetLength(1);
        var r = new double[n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < m; j++)
                r[i] += a[i, j] * x[j];
        return r;
    }

    public static double Dot(double[] a, double[] b)
    {
        var s = 0.0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

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
                if (Math.Abs(aug[r, col]) > Math.Abs(aug[piv, col])) piv = r;
            if (Math.Abs(aug[piv, col]) < 1e-14)
                throw new InvalidOperationException("Matrix is singular");
            for (var j = 0; j < 2 * n; j++) (aug[col, j], aug[piv, j]) = (aug[piv, j], aug[col, j]);
            var d = aug[col, col];
            for (var j = 0; j < 2 * n; j++) aug[col, j] /= d;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = aug[r, col];
                for (var j = 0; j < 2 * n; j++) aug[r, j] -= f * aug[col, j];
            }
        }
        var inv = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                inv[i, j] = aug[i, n + j];
        return inv;
    }

    public static double[] JacobiEigenvalues(double[,] sym)
    {
        var n = sym.GetLength(0);
        var a = (double[,])sym.Clone();
        for (var sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                    off += a[p, q] * a[p, q];
            if (off < 1e-20) break;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (Math.Abs(a[p, q]) < 1e-15) continue;
                var theta = 0.5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
                var c = Math.Cos(theta);
                var s = Math.Sin(theta);
                for (var k = 0; k < n; k++)
                {
                    var akp = a[k, p];
                    var akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (var k = 0; k < n; k++)
                {
                    var apk = a[p, k];
                    var aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
            }
        }
        var ev = new double[n];
        for (var i = 0; i < n; i++) ev[i] = a[i, i];
        Array.Sort(ev);
        return ev;
    }

    /// <summary>Condition number of a (dims x k) endmember matrix via singular values.</summary>
    public static double ConditionNumber(double[,] m)
    {
        var dims = m.GetLength(0);
        var k = m.GetLength(1);
        var mtm = new double[k, k];
        for (var i = 0; i < k; i++)
        for (var j = 0; j < k; j++)
        {
            double s = 0;
            for (var d = 0; d < dims; d++) s += m[d, i] * m[d, j];
            mtm[i, j] = s;
        }
        var ev = JacobiEigenvalues(mtm);
        var max = Math.Sqrt(Math.Max(ev[^1], 0));
        var min = Math.Sqrt(Math.Max(ev[0], 0));
        if (max <= 0) return double.PositiveInfinity;
        return min <= 0 ? double.PositiveInfinity : max / min;
    }

    public static double Mahalanobis2(double[] r, double[,] cov)
    {
        var inv = Inverse(cov);
        var n = r.Length;
        double s = 0;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
            s += r[i] * inv[i, j] * r[j];
        return s;
    }
}

namespace IsoMix.Domain;

public static class Linalg
{
    public static double[] MatVec(double[][] a, double[] x)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < x.Length; j++)
                r[i] += a[i][j] * x[j];
        return r;
    }

    public static double[][] Add(double[][] a, double[][] b)
    {
        var r = new double[a.Length][];
        for (var i = 0; i < a.Length; i++)
        {
            r[i] = new double[a[i].Length];
            for (var j = 0; j < a[i].Length; j++) r[i][j] = a[i][j] + b[i][j];
        }
        return r;
    }

    public static double[][] Identity(int n)
    {
        var r = new double[n][];
        for (var i = 0; i < n; i++)
        {
            r[i] = new double[n];
            r[i][i] = 1.0;
        }
        return r;
    }

    public static double[][] Inverse(double[][] a)
    {
        var n = a.Length;
        var aug = new double[n][];
        for (var i = 0; i < n; i++)
        {
            aug[i] = new double[2 * n];
            for (var j = 0; j < n; j++) aug[i][j] = a[i][j];
            aug[i][n + i] = 1.0;
        }
        for (var col = 0; col < n; col++)
        {
            var piv = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[piv][col])) piv = r;
            if (Math.Abs(aug[piv][col]) < 1e-14)
                throw new InvalidOperationException("matrix is singular");
            (aug[col], aug[piv]) = (aug[piv], aug[col]);
            var d = aug[col][col];
            for (var j = 0; j < 2 * n; j++) aug[col][j] /= d;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = aug[r][col];
                if (f == 0) continue;
                for (var j = 0; j < 2 * n; j++) aug[r][j] -= f * aug[col][j];
            }
        }
        var inv = new double[n][];
        for (var i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            for (var j = 0; j < n; j++) inv[i][j] = aug[i][n + j];
        }
        return inv;
    }

    public static double Mahalanobis(double[] residual, double[][] covariance)
    {
        var inv = Inverse(covariance);
        var m = MatVec(inv, residual);
        var d2 = 0.0;
        for (var i = 0; i < residual.Length; i++) d2 += residual[i] * m[i];
        return d2;
    }

    public static double MinSingularValue(double[][] a)
    {
        var m = a.Length;
        var n = a[0].Length;
        var ata = new double[n][];
        for (var i = 0; i < n; i++) ata[i] = new double[n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                for (var k = 0; k < m; k++)
                    ata[i][j] += a[k][i] * a[k][j];
        var evals = JacobiEigenvalues(ata);
        var min = double.MaxValue;
        foreach (var e in evals) min = Math.Min(min, Math.Max(e, 0.0));
        return Math.Sqrt(min);
    }

    public static double[] JacobiEigenvalues(double[][] sym)
    {
        var n = sym.Length;
        var a = sym.Select(r => (double[])r.Clone()).ToArray();
        for (var sweep = 0; sweep < 100; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                    off += a[p][q] * a[p][q];
            if (off < 1e-20) break;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p][q]) < 1e-15) continue;
                    var theta = 0.5 * Math.Atan2(2 * a[p][q], a[q][q] - a[p][p]);
                    var c = Math.Cos(theta);
                    var s = Math.Sin(theta);
                    for (var k = 0; k < n; k++)
                    {
                        var akp = a[k][p];
                        var akq = a[k][q];
                        a[k][p] = c * akp - s * akq;
                        a[k][q] = s * akp + c * akq;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var apk = a[p][k];
                        var aqk = a[q][k];
                        a[p][k] = c * apk - s * aqk;
                        a[q][k] = s * apk + c * aqk;
                    }
                }
        }
        var d = new double[n];
        for (var i = 0; i < n; i++) d[i] = a[i][i];
        return d;
    }
}

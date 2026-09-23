namespace IsotopeMixing.Solver;

public static class LinearAlgebra
{
    public static double[][] Identity(int n)
    {
        var m = new double[n][];
        for (int i = 0; i < n; i++) { m[i] = new double[n]; m[i][i] = 1; }
        return m;
    }

    public static double[][] Transpose(double[][] a)
    {
        int r = a.Length, c = a[0].Length;
        var t = Rect(c, r);
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
                t[j][i] = a[i][j];
        return t;
    }

    public static double[][] Rect(int r, int c)
    {
        var m = new double[r][];
        for (int i = 0; i < r; i++) m[i] = new double[c];
        return m;
    }

    public static double[][] Mul(double[][] a, double[][] b)
    {
        int n = a.Length, k = b.Length, m = b[0].Length;
        var c = Rect(n, m);
        for (int i = 0; i < n; i++)
            for (int p = 0; p < k; p++)
            {
                double aip = a[i][p];
                if (aip == 0) continue;
                for (int j = 0; j < m; j++) c[i][j] += aip * b[p][j];
            }
        return c;
    }

    public static double[] Mul(double[][] a, double[] x)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            double s = 0;
            for (int j = 0; j < x.Length; j++) s += a[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double[][]? Cholesky(double[][] a)
    {
        int n = a.Length;
        var l = Rect(n, n);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double s = a[i][j];
                for (int p = 0; p < j; p++) s -= l[i][p] * l[j][p];
                if (i == j)
                {
                    if (s <= 1e-12) return null;
                    l[i][j] = Math.Sqrt(s);
                }
                else l[i][j] = s / l[j][j];
            }
        }
        return l;
    }

    public static double[][] InvertSPD(double[][] a)
    {
        int n = a.Length;
        var l = Cholesky(a) ?? throw new ArgumentException("matrix not SPD");
        var inv = Rect(n, n);
        var col = new double[n];
        for (int c = 0; c < n; c++)
        {
            col[c] = 1;
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = col[i];
                for (int j = 0; j < i; j++) s -= l[i][j] * y[j];
                y[i] = s / l[i][i];
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = y[i];
                for (int j = i + 1; j < n; j++) s -= l[j][i] * x[j];
                x[i] = s / l[i][i];
            }
            for (int i = 0; i < n; i++) inv[i][c] = x[i];
            col[c] = 0;
        }
        return inv;
    }

    public static (double[] values, double[][] vectors) SymmetricEigen(double[][] a)
    {
        int n = a.Length;
        var v = Identity(n);
        var d = new double[n];
        var m = a.Select(r => (double[])r.Clone()).ToArray();
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p][q] * m[p][q];
            if (off < 1e-28) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(m[p][q]) < 1e-15) continue;
                    double tau = (m[q][q] - m[p][p]) / (2 * m[p][q]);
                    double t = Math.Sign(tau == 0 ? 1 : tau) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1 / Math.Sqrt(1 + t * t), s = t * c;
                    for (int k = 0; k < n; k++)
                    {
                        double mpk = m[p][k], mqk = m[q][k];
                        m[p][k] = c * mpk - s * mqk;
                        m[q][k] = s * mpk + c * mqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double mkp = m[k][p], mkq = m[k][q];
                        m[k][p] = c * mkp - s * mkq;
                        m[k][q] = s * mkp + c * mkq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k][p], vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
        }
        for (int i = 0; i < n; i++) d[i] = m[i][i];
        return (d, v);
    }

    public static (int rank, List<double[]> nullspace) Nullspace(double[][] a, double tol = 1e-9)
    {
        var ata = Mul(Transpose(a), a);
        var (vals, vecs) = SymmetricEigen(ata);
        double scale = Math.Sqrt(Math.Max(1e-30, vals.Max()));
        var nul = new List<double[]>();
        int rank = 0;
        for (int i = 0; i < vals.Length; i++)
        {
            if (vals[i] <= tol * scale * scale)
            {
                var z = new double[vals.Length];
                for (int r = 0; r < vals.Length; r++) z[r] = vecs[r][i];
                nul.Add(z);
            }
            else rank++;
        }
        return (rank, nul);
    }

    public static double ChiSquareSurvival(double x, int dof)
    {
        if (dof <= 0) return double.NaN;
        if (x <= 0) return 1.0;
        return GammaUpperRegularized(dof / 2.0, x / 2.0);
    }

    public static double GammaUpperRegularized(double a, double x)
    {
        if (x < a + 1) return 1 - GammaPSeries(a, x);
        return GammaQContinuedFraction(a, x);
    }

    private static double GammaPSeries(double a, double x)
    {
        double ap = a, sum = 1 / a, delta = sum;
        for (int n = 1; n <= 200; n++)
        {
            ap += 1;
            delta *= x / ap;
            sum += delta;
            if (Math.Abs(delta) < Math.Abs(sum) * 1e-14) break;
        }
        return sum * Math.Exp(-x + a * Math.Log(x) - GammaLn(a));
    }

    private static double GammaQContinuedFraction(double a, double x)
    {
        double b = x + 1 - a, c = 1e300, d = 1 / b, h = d;
        for (int i = 1; i <= 200; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-14) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - GammaLn(a)) * h;
    }

    private static double GammaLn(double z)
    {
        double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double x = z, y = z, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += c[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }
}

namespace IsotopeMix.Core.LinearAlgebra;

/// <summary>卡方分布工具：可行域阈值按测量维自由度取分位数。</summary>
public static class ChiSquare
{
    /// <summary>自由度 df 的卡方分布 p 分位数（对 d≤3 足够精确的二分求逆）。</summary>
    public static double Quantile(double p, int df)
    {
        double lo = 0, hi = Math.Max(2, df * 4.0);
        while (Cdf(hi, df) < p) hi *= 2;
        for (int i = 0; i < 80; i++)
        {
            double mid = (lo + hi) / 2;
            if (Cdf(mid, df) < p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>P(χ²_df ≤ x)，正则化下不完全伽马级数。</summary>
    public static double Cdf(double x, int df)
    {
        if (x <= 0 || df <= 0) return 0;
        double a = df / 2.0, z = x / 2.0;
        if (z < a + 1)
        {
            double term = 1.0 / a, sum = term;
            for (int n = 1; n < 200; n++)
            {
                term *= z / (a + n);
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * 1e-15) break;
            }
            return sum * Math.Exp(-z + a * Math.Log(z) - LogGamma(a));
        }
        return 1 - UpperSeries(z, a);
    }

    private static double UpperSeries(double z, double a)
    {
        double b = z + 1 - a, c = 1e30, d = 1 / b, h = d;
        for (int i = 1; i < 200; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = b + an / c;
            if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-15) break;
        }
        return Math.Exp(-z + a * Math.Log(z) - LogGamma(a)) * h;
    }

    private static double LogGamma(double z)
    {
        double[] c =
        {
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5
        };
        double x = z, y = z;
        double tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += c[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }
}

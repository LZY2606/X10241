namespace IsotopeMixing.Core;

/// <summary>卡方分布：上侧概率与 0.95 分位数，用于残差显著性判断。</summary>
public static class ChiSquare
{
    // 正则化下侧不完全伽马 P(a,x)
    private static double GammaP(double a, double x)
    {
        if (x <= 0) return 0;
        if (x < a + 1)
        {
            // 级数展开
            double ap = a, sum = 1 / a, del = 1 / a;
            for (int n = 0; n < 200; n++)
            {
                ap += 1;
                del *= x / ap;
                sum += del;
                if (Math.Abs(del) < Math.Abs(sum) * 1e-15) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }
        else
        {
            // 连分式展开
            double b = x + 1 - a, c = double.MaxValue, d = 1 / b, h = d;
            for (int i = 1; i <= 200; i++)
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
                if (Math.Abs(del - 1) < 1e-14) break;
            }
            return 1 - Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
        }
    }

    private static double LogGamma(double z)
    {
        double[] coef =
        {
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5
        };
        double x = z, y = z;
        double tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < coef.Length; j++) ser += coef[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    /// <summary>上侧（生存）概率 P(chi² &gt;= x)。</summary>
    public static double PValue(double chi2, int dof)
    {
        if (dof <= 0) return chi2 > 1e-9 ? 0.0 : 1.0;
        return Math.Clamp(1 - GammaP(dof / 2.0, chi2 / 2.0), 0, 1);
    }

    /// <summary>二分查找卡方 0.95 分位数。</summary>
    public static double Critical95(int dof)
    {
        if (dof <= 0) return 0;
        double lo = 0, hi = Math.Max(4.0, dof * 3.0);
        while (PValue(hi, dof) > 0.05) hi *= 2;
        for (int i = 0; i < 80; i++)
        {
            double mid = (lo + hi) / 2;
            if (PValue(mid, dof) < 0.05) hi = mid;
            else lo = mid;
        }
        return (lo + hi) / 2;
    }
}

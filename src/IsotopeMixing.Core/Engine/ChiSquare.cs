using System;

namespace IsotopeMixing.Core.Engine;

/// <summary>卡方临界值：下半 gamma 反函数（Newton + 级数），避免外部数值依赖。</summary>
public static class ChiSquare
{
    public static double CriticalValue(double confidence, int degreesOfFreedom)
    {
        double p = confidence;
        if (p <= 0 || p >= 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        double k = Math.Max(degreesOfFreedom, 1);
        double a = k / 2.0, x = k;
        // Wilson–Hilferty 初值
        double z = Probit(p);
        x = k * Math.Pow(1 - 2.0 / (9 * k) + z * Math.Sqrt(2.0 / (9 * k)), 3);
        x = Math.Max(x, 1e-6);
        for (int it = 0; it < 60; it++)
        {
            double f = GammaP(a, x / 2.0) - p;
            double fp = 0.5 * Math.Pow(x / 2.0, a - 1) * Math.Exp(-x / 2.0) / Gamma(a);
            if (Math.Abs(fp) < 1e-300) break;
            double nx = x - f / fp;
            if (nx <= 0) nx = x / 2;
            if (Math.Abs(nx - x) < 1e-10 * Math.Max(1, x)) { x = nx; break; }
            x = nx;
        }
        return x;
    }

    public static double LowerGammaP(double a, double x) => GammaP(a, x);

    private static double GammaP(double a, double x)
    {
        if (x < a + 1)
        {
            // 级数表示
            double ap = a, sum = 1 / a, delta = sum;
            for (int n = 0; n < 300; n++)
            {
                ap += 1;
                delta *= x / ap;
                sum += delta;
                if (Math.Abs(delta) < Math.Abs(sum) * 1e-14) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - GammaLog(a));
        }
        // 连分数表示 Q，再 P = 1 - Q
        double b = x + 1 - a;
        double c = 1e300, d = 1 / b, h = d;
        for (int i = 1; i < 300; i++)
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
        return 1 - Math.Exp(-x + a * Math.Log(x) - GammaLog(a)) * h;
    }

    private static double GammaLog(double x)
    {
        double[] coef = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
                          -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += coef[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    private static double Gamma(double x) => Math.Exp(GammaLog(x));

    /// <summary>标准正态分位数（Acklam 近似）。</summary>
    private static double Probit(double p)
    {
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
                        1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
                        6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
                       -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
                       3.754408661907416e+00 };
        double plow = 0.02425, phigh = 1 - plow, q, r, x;
        if (p < plow)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= phigh)
        {
            q = p - 0.5; r = q * q;
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        return x;
    }
}

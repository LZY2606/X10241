using System;

namespace IsotopeMixing.Core.Math;

// 卡方分位数（自由度 1~4），用于“样品是否落在测量协方差+端元方差允许范围”。
// 相关误差通过完整协方差进入 Mahalanobis 距离；阈值只取决于自由度。
public static class ChiSquare
{
    public static double CriticalValue(int degreesOfFreedom, double confidence = 0.95)
    {
        double target = confidence;
        // 对分位数做二分搜索：CDF(自由度/2, x/2) = confidence。
        double lo = 0;
        double hi = System.Math.Max(4.0, degreesOfFreedom * 4.0);
        while (RegularizedGammaP(df: degreesOfFreedom / 2.0, x: hi / 2.0) < target) hi *= 2;
        for (int i = 0; i < 80; i++)
        {
            double mid = (lo + hi) / 2;
            if (RegularizedGammaP(degreesOfFreedom / 2.0, mid / 2.0) < target) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2;
    }

    // Lanczos Gamma，来自 Numerical Recipes（系数 7 位精度，足够判定可行域边界）。
    private static readonly double[] Lanczos =
    {
        0.99999999999980993, 676.5203681218851, -1259.1392167224028,
        771.32342877765313, -176.61502916214059, 12.507343278686905,
        -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7
    };

    private static double Gamma(double z)
    {
        if (z < 0.5)
        {
            return System.Math.PI /
                   (System.Math.Sin(System.Math.PI * z) * Gamma(1 - z));
        }
        z -= 1;
        double x = Lanczos[0];
        for (int i = 1; i < Lanczos.Length; i++) x += Lanczos[i] / (z + i);
        double t = z + Lanczos.Length - 1.5;
        return System.Math.Sqrt(2 * System.Math.PI) * System.Math.Pow(t, z + 0.5) *
               System.Math.Exp(-t) * x;
    }

    private static double RegularizedGammaP(double df, double x)
    {
        if (x <= 0) return 0;
        if (x < df + 1)
        {
            // 级数展开
            double ap = 1.0 / df;
            double sum = ap;
            for (int n = 1; n < 200; n++)
            {
                ap *= x / (df + n);
                sum += ap;
                if (System.Math.Abs(ap) < System.Math.Abs(sum) * 1e-14) break;
            }
            return sum * System.Math.Exp(-x + df * System.Math.Log(x) - System.Math.Log(Gamma(df)));
        }

        // 连分式展开（上侧正则化，再取补）
        double b = x + 1 - df;
        double c = double.MaxValue;
        double d = 1 / b;
        double h = d;
        for (int i = 1; i < 200; i++)
        {
            double an = -i * (i - df);
            b += 2;
            d = an * d + b;
            if (System.Math.Abs(d) < 1e-30) d = 1e-30;
            c = b + an / c;
            if (System.Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d;
            double delta = d * c;
            h *= delta;
            if (System.Math.Abs(delta - 1) < 1e-14) break;
        }
        return 1 - System.Math.Exp(-x + df * System.Math.Log(x) - System.Math.Log(Gamma(df))) * h;
    }
}

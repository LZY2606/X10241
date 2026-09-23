namespace IsoMix.Domain;

public static class ChiSquare
{
    public static double RegularizedGammaP(double a, double x)
    {
        if (x <= 0) return 0;
        if (x < a + 1)
        {
            double ap = a, sum = 1.0 / a, del = sum;
            for (int n = 0; n < 1000; n++)
            {
                ap += 1;
                del *= x / ap;
                sum += del;
                if (Math.Abs(del) < Math.Abs(sum) * 1e-14) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }
        return 1 - RegularizedGammaQ(a, x);
    }

    public static double RegularizedGammaQ(double a, double x)
    {
        if (x <= 0) return 1;
        double b = x + 1 - a, c = 1e300, d = 1 / b, h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b; if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c; if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-14) break;
        }
        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    public static double LogGamma(double x)
    {
        double[] coef = { 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012,
            9.9843695780195716e-6, 1.5056327351493116e-7 };
        if (x < 0.5)
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        x -= 1;
        double t = 0.99999999999980993;
        for (int i = 0; i < coef.Length; i++) t += coef[i] / (x + i + 1);
        double g = 7.5;
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(x + g) - (x + g) + Math.Log(t);
    }

    public static double Quantile(double p, int degreesOfFreedom)
    {
        double lo = 0, hi = Math.Max(10, degreesOfFreedom * 4 + 40);
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            double cdf = RegularizedGammaP(degreesOfFreedom / 2.0, mid / 2.0);
            if (cdf < p) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}

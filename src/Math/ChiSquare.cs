namespace IsotopeMix.MathN;

// Chi-square CDF and inverse CDF without external dependencies.
// Uses Lanczos gamma + regularized lower incomplete gamma series/continued
// fraction, with a Newton loop for the quantile.
public static class ChiSquare
{
    private const double Eps = 1e-14;

    public static double LogGamma(double x)
    {
        double[] coef =
        {
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5
        };
        double y = x;
        double tmp = x + 5.5;
        tmp -= (x + 0.5) * System.Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < coef.Length; j++) ser += coef[j] / ++y;
        return -tmp + System.Math.Log(2.5066282746310005 * ser / x);
    }

    public static double GammaSeries(double a, double x)
    {
        double ap = a;
        double sum = 1 / a;
        double delta = sum;
        for (int n = 1; n < 300; n++)
        {
            ap += 1;
            delta *= x / ap;
            sum += delta;
            if (System.Math.Abs(delta) < System.Math.Abs(sum) * Eps) break;
        }
        return sum * System.Math.Exp(-x + a * System.Math.Log(x) - LogGamma(a));
    }

    public static double GammaCf(double a, double x)
    {
        double b = x + 1 - a;
        double c = 1e300;
        double d = 1 / b;
        double h = d;
        for (int i = 1; i < 300; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (System.Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c;
            if (System.Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (System.Math.Abs(del - 1) < Eps) break;
        }
        return System.Math.Exp(-x + a * System.Math.Log(x) - LogGamma(a)) * h;
    }

    public static double Gammp(double a, double x)
    {
        if (x < 0) return 0;
        if (x < a + 1) return GammaSeries(a, x);
        return 1 - GammaCf(a, x);
    }

    public static double Cdf(double x, double df) => Gammp(df / 2, x / 2);

    public static double InverseCdf(double p, double df)
    {
        if (p <= 0 || p >= 1) return double.NaN;
        // Starting guess (Wilson-Hilferty), then Newton iterations.
        double z = QuantileStandardNormal(p);
        double t = 2.0 / (9 * df);
        double x = df * System.Math.Pow(1 - t + z * System.Math.Sqrt(t), 3);
        if (x < 1e-6) x = 1e-6;
        for (int it = 0; it < 40; it++)
        {
            double c = Cdf(x, df);
            double diff = c - p;
            if (System.Math.Abs(diff) < 1e-12) break;
            double pdf = System.Math.Exp((df / 2 - 1) * System.Math.Log(x) - x / 2 -
                                         (df / 2) * System.Math.Log(2) - LogGamma(df / 2));
            if (pdf < 1e-300) break;
            double nx = x - diff / pdf;
            if (nx <= 0) nx = x / 2;
            if (System.Math.Abs(nx - x) < 1e-12 * System.Math.Abs(x)) { x = nx; break; }
            x = nx;
        }
        return x;
    }

    // Acklam inverse normal CDF approximation.
    public static double QuantileStandardNormal(double p)
    {
        const double a1 = -3.969683028665376e+01, a2 = 2.209460984245205e+02;
        const double a3 = -2.759285104469687e+02, a4 = 1.383577518672690e+02;
        const double a5 = -3.066479806614716e+01, a6 = 2.506628277459239e+00;
        const double b1 = -5.447609879822406e+01, b2 = 1.615858368580409e+02;
        const double b3 = -1.556989798598866e+02, b4 = 6.680131188771972e+01;
        const double b5 = -1.328068155288572e+01;
        const double c1 = -7.784894002430293e-03, c2 = -3.223964580411365e-01;
        const double c3 = -2.400758277161838e+00, c4 = -2.549732539343734e+00;
        const double c5 = 4.374664141464968e+00, c6 = 2.938163982698783e+00;
        const double d1 = 7.784695709041462e-03, d2 = 3.224671290700398e-01;
        const double d3 = 2.445134137142996e+00, d4 = 3.754408661907416e+00;
        const double plow = 0.02425, phigh = 1 - plow;
        double q, r;
        if (p < plow)
        {
            q = System.Math.Sqrt(-2 * System.Math.Log(p));
            return (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                   ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
        }
        if (p <= phigh)
        {
            q = p - 0.5; r = q * q;
            return (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * q /
                   (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1);
        }
        q = System.Math.Sqrt(-2 * System.Math.Log(1 - p));
        return -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
    }
}

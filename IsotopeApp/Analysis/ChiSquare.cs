namespace IsotopeApp.Analysis;

/// <summary>Chi-squared quantiles via regularised gamma, no external dependency.</summary>
public static class ChiSquare
{
    /// <summary>Lower-tail regularised gamma P(a,x) with continued-fraction tail.</summary>
    public static double GammaP(double a, double x)
    {
        if (x < 0) return 0;
        if (x < a + 1)
        {
            double ap = a, sum = 1 / a, del = sum;
            for (int i = 0; i < 300; i++)
            {
                ap += 1;
                del *= x / ap;
                sum += del;
                if (Math.Abs(del) < Math.Abs(sum) * 1e-15) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }
        double b = x + 1 - a, c = 1e300, d = 1 / b, h = d;
        for (int i = 1; i < 300; i++)
        {
            double an = -i * (i - a);
            b += 2;
            d = an * d + b;
            if (Math.Abs(d) < 1e-300) d = 1e-300;
            c = b + an / c;
            if (Math.Abs(c) < 1e-300) c = 1e-300;
            d = 1 / d;
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < 1e-15) break;
        }
        return 1 - Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    public static double InverseCdf(double p, int df)
    {
        // Wilson-Hilferty seed, Newton refinement on the regularised gamma.
        double z = NormalQuantile(p);
        double x = df * Math.Pow(1 - 2.0 / (9 * df) + z * Math.Sqrt(2.0 / (9 * df)), 3);
        if (x <= 0) x = 0.05;
        for (int it = 0; it < 60; it++)
        {
            double f = GammaP(df / 2.0, x / 2.0) - p;
            double dens = Math.Exp(-x / 2.0 + (df / 2.0 - 1) * Math.Log(x / 2.0) -
                                   LogGamma(df / 2.0)) / 2.0;
            if (dens <= 1e-300) break;
            double nx = x - f / dens;
            if (nx <= 0) nx = x / 2;
            if (Math.Abs(nx - x) < 1e-12 * Math.Max(1, x)) { x = nx; break; }
            x = nx;
        }
        return x;
    }

    /// <summary>log Gamma via Lanczos (Numerical Recipes), high precision.</summary>
    public static double LogGamma(double x)
    {
        double[] cof =
        {
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5
        };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        foreach (double c in cof) { y += 1; ser += c / y; }
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }

    public static double NormalQuantile(double p)
    {
        // Peter Acklam's inverse-normal approximation.
        double[] a =
        {
            -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
            1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00
        };
        double[] b =
        {
            -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
            6.680131188771972e+01, -1.328068155288572e+01
        };
        double[] c =
        {
            -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
            -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00
        };
        double[] d =
        {
            7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
            3.754408661907416e+00
        };
        const double plow = 0.02425, phigh = 1 - plow;
        double q, r;
        if (p < plow)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p <= phigh)
        {
            q = p - 0.5; r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                   (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        q = Math.Sqrt(-2 * Math.Log(1 - p));
        return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
               ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
    }
}

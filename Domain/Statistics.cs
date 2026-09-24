namespace IsotopeMix.Domain;

public static class Statistics
{
    /// <summary>χ² 分位数。dof≤2 用闭式，dof≥3 用 Wilson–Hilferty 近似（阈值精度对可视化足够）。</summary>
    public static double ChiSquareQuantile(double p, int dof)
    {
        if (dof <= 0) return 0;
        if (dof == 1)
        {
            double z = NormalQuantile(0.5 + p / 2);
            return z * z;
        }
        if (dof == 2) return -2.0 * Math.Log(1 - p);

        double z = NormalQuantile(p);
        double h = 2.0 / (9.0 * dof);
        double t = 1 - h + z * Math.Sqrt(h);
        return dof * t * t * t;
    }

    /// <summary>Acklam 有理逼近的标准正态分位数。</summary>
    public static double NormalQuantile(double p)
    {
        p = Math.Clamp(p, 1e-12, 1 - 1e-12);
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };

        double pLow = 0.02425, pHigh = 1 - pLow;
        double q, r;
        if (p < pLow)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p <= pHigh)
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

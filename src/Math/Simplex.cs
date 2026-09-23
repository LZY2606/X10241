namespace IsotopeMix.MathN;

// Euclidean projection onto the probability simplex {p >= 0, sum p = 1}
// (Duchi et al. 2008). This is used ONLY inside the constrained optimizer;
// it is never applied post hoc to an unconstrained solution and then reported
// as if the simplex constraints had been part of the inference.
public static class Simplex
{
    public static double[] Project(double[] v)
    {
        int n = v.Length;
        var u = (double[])v.Clone();
        Array.Sort(u, System.Collections.Generic.Comparer<double>.Create((a, b) => b.CompareTo(a)));
        double cssv = 0;
        double rho = 0;
        for (int i = 0; i < n; i++)
        {
            cssv += u[i];
            if (u[i] - (cssv - 1) / (i + 1) > 0) rho = i;
        }
        double theta = (() =>
        {
            double s = 0;
            for (int i = 0; i <= (int)rho; i++) s += u[i];
            return (s - 1) / (rho + 1);
        })();
        var w = new double[n];
        for (int i = 0; i < n; i++) w[i] = System.Math.Max(v[i] - theta, 0);
        return w;
    }

    public static bool IsSimplex(double[] p, double tol = 1e-9)
    {
        double sum = 0;
        foreach (var x in p)
        {
            if (x < -tol) return false;
            sum += x;
        }
        return System.Math.Abs(sum - 1) < tol;
    }
}

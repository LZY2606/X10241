using System;

namespace IsoMix.Core.Solver;

/// <summary>
/// Small, dependency-free linear algebra for the dimensions used in isotope mixing
/// (a handful of isotopes / sources). Eigen-decomposition is the classic cyclic
/// Jacobi rotation, which is exact enough at 2x2-4x4 and keeps covariance
/// propagation explicit.
/// </summary>
public static class LinearAlgebra
{
    public static (double[] eigenvalues, double[,] eigenvectors) JacobiEigen(double[,] aIn, int maxSweeps = 100)
    {
        int n = aIn.GetLength(0);
        var a = (double[,])aIn.Clone();
        var v = new double[n, n];
        for (int i = 0; i < n; i++) v[i, i] = 1.0;

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += a[p, q] * a[p, q];
            if (off < 1e-24) break;

            for (int p = 0; p < n; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p, q];
                    if (Math.Abs(apq) < 1e-15) continue;
                    double app = a[p, p];
                    double aqq = a[q, q];
                    double phi = 0.5 * Math.Atan2(2 * apq, aqq - app);
                    double c = Math.Cos(phi);
                    double s = Math.Sin(phi);

                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k, p];
                        double akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[p, k];
                        double aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double vkp = v[k, p];
                        double vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }
        }

        var vals = new double[n];
        for (int i = 0; i < n; i++) vals[i] = a[i, i];
        Array.Sort(vals);
        return (vals, v);
    }

    /// <summary>
    /// Inverse of a symmetric positive (semi-)definite matrix. Eigenvalues at or
    /// below the floor are floored before inversion; the floor is reported via
    /// <paramref name="floorApplied"/> so callers can tell that uncertainty was
    /// singular rather than hiding it.
    /// </summary>
    public static double[,] SymmetricInverse(double[,] m, double floor = 1e-10, out bool floorApplied)
    {
        int n = m.GetLength(0);
        var (vals, vecs) = JacobiEigen(m);
        floorApplied = false;
        double max = 0;
        foreach (var x in vals) max = Math.Max(max, Math.Abs(x));
        double lim = Math.Max(floor, max * 1e-10);
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            double lam = vals[i];
            if (lam < lim) { lam = lim; floorApplied = true; }
            double invLam = 1.0 / lam;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    inv[r, c] += vecs[r, i] * vecs[c, i] * invLam;
        }
        return inv;
    }

    public static double MahalanobisSquared(double[] residual, double[,] covariance)
    {
        int n = residual.Length;
        var inv = SymmetricInverse(covariance, out _);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double row = 0;
            for (int j = 0; j < n; j++) row += inv[i, j] * residual[j];
            sum += residual[i] * row;
        }
        return sum;
    }

    public static bool HasOffDiagonal(double[,] covariance, double tolerance = 1e-12)
    {
        int n = covariance.GetLength(0);
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Math.Abs(covariance[i, j]) > tolerance) return true;
        return false;
    }

    /// <summary>Singular values of A via the eigenvalues of A^T A.</summary>
    public static double[] SingularValues(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var ata = new double[cols, cols];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < cols; j++)
            {
                double s = 0;
                for (int r = 0; r < rows; r++) s += a[r, i] * a[r, j];
                ata[i, j] = s;
            }
        var (vals, _) = JacobiEigen(ata);
        for (int i = 0; i < vals.Length; i++) vals[i] = Math.Sqrt(Math.Max(0, vals[i]));
        Array.Sort(vals);
        return vals;
    }

    public static double[,] Add(double[,] a, double[,] b)
    {
        int n = a.GetLength(0);
        var r = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                r[i, j] = a[i, j] + b[i, j];
        return r;
    }

    /// <summary>
    /// Euclidean projection of x onto the probability simplex
    /// {p : p &gt;= 0, sum p = 1}. Used only to find the envelope's nearest legal
    /// point — never to manufacture a "solution" from a truncated/renormalized
    /// unconstrained estimate.
    /// </summary>
    public static double[] ProjectSimplex(double[] x)
    {
        int n = x.Length;
        var u = (double[])x.Clone();
        Array.Sort(u, (a, b) => b.CompareTo(a));
        double cssv = 0;
        double rho = 0;
        for (int i = 0; i < n; i++)
        {
            cssv += u[i];
            double cand = u[i] - (cssv - 1) / (i + 1);
            if (cand > 0) rho = i;
        }
        double sumTop = 0;
        for (int i = 0; i <= rho; i++) sumTop += u[i];
        double theta = (sumTop - 1) / (rho + 1);
        var p = new double[n];
        for (int i = 0; i < n; i++) p[i] = Math.Max(0, x[i] - theta);
        return p;
    }

    /// <summary>Upper-tail chi-square critical value by Wilson-Hilferty approximation.</summary>
    public static double ChiSquareCritical(double confidenceLevel, int degreesOfFreedom)
    {
        double p = 1 - confidenceLevel;
        double z = NormalQuantile(1 - p / 2);
        double k = degreesOfFreedom;
        double inner = 1 - 2.0 / (9 * k) + z * Math.Sqrt(2.0 / (9 * k));
        return k * inner * inner * inner;
    }

    /// <summary>Inverse standard normal CDF (Acklam), sufficient for 0.90-0.999 levels.</summary>
    public static double NormalQuantile(double p)
    {
        const double a1 = -39.6968302866538, a2 = 220.946098424521, a3 = -275.928510446969;
        const double a4 = 138.357751867269, a5 = -30.6647980661472, a6 = 2.50662827745924;
        const double b1 = -54.4760987982241, b2 = 161.585836858041, b3 = -155.698979859887;
        const double b4 = 66.8013118877197, b5 = -13.2806815528857;
        const double c1 = -0.00778489400243029, c2 = -0.322396458041136, c3 = -2.40075827716184;
        const double c4 = -2.54973253934373, c5 = 4.37466414146497, c6 = 2.93816398269878;
        const double d1 = 0.00778469570904146, d2 = 0.32246712907004, d3 = 2.445134137143;
        const double d4 = 3.75440866190742;
        const double pLow = 0.02425, pHigh = 1 - pLow;

        double q, r;
        if (p < pLow)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                   ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
        }
        if (p <= pHigh)
        {
            q = p - 0.5;
            r = q * q;
            return (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * q /
                   (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1);
        }
        q = Math.Sqrt(-2 * Math.Log(1 - p));
        return -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
    }
}

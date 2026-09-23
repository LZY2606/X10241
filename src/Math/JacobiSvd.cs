namespace IsotopeMix.MathN;

// Jacobi eigendecomposition of a real symmetric matrix. Small matrices only
// (source counts <= 6), so a dependency-free implementation is appropriate.
public static class JacobiEigen
{
    public static (double[] values, double[,] vectors) Decompose(double[,] a, int maxSweeps = 100)
    {
        int n = a.GetLength(0);
        var v = new double[n, n];
        var d = new double[n];
        var b = new double[n];
        var z = new double[n];
        for (int p = 0; p < n; p++)
        {
            v[p, p] = 1.0;
            d[p] = a[p, p];
            b[p] = d[p];
        }

        for (int sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++)
                    off += System.Math.Abs(a[p, q]);
            if (off < 1e-14) break;

            for (int p = 0; p < n - 1; p++)
            {
                for (int q = p + 1; q < n; q++)
                {
                    double apq = a[p, q];
                    if (System.Math.Abs(apq) < 1e-15) continue;
                    double gap = d[q] - d[p];
                    double t;
                    if (System.Math.Abs(gap) < 1e-300)
                    {
                        double theta = (gap >= 0 ? System.Math.PI / 4 : -System.Math.PI / 4);
                        t = System.Math.Tan(theta / 2);
                    }
                    else
                    {
                        double theta = System.Math.Atan2(2 * apq, gap) / 2;
                        t = System.Math.Tan(theta);
                    }
                    double c = 1.0 / System.Math.Sqrt(1 + t * t);
                    double s = t * c;
                    double tau = s / (1 + c);
                    d[p] -= t * apq;
                    d[q] += t * apq;
                    a[p, q] = 0;

                    for (int r = 0; r < p; r++) Rotate(a, r, p, r, q, s, tau);
                    for (int r = p + 1; r < q; r++) Rotate(a, p, r, r, q, s, tau);
                    for (int r = q + 1; r < n; r++) Rotate(a, p, r, q, r, s, tau);
                    for (int r = 0; r < n; r++)
                    {
                        double vrp = v[r, p];
                        double vrq = v[r, q];
                        v[r, p] = vrp - s * (vrq + tau * vrp);
                        v[r, q] = vrq + s * (vrp - tau * vrq);
                    }
                }
            }
            for (int p = 0; p < n; p++)
            {
                b[p] += z[p];
                d[p] = b[p];
                z[p] = 0;
            }
        }

        // Sort eigenvalues descending, carry eigenvectors.
        var order = Enumerable.Range(0, n).OrderByDescending(i => d[i]).ToArray();
        var vals = new double[n];
        var vecs = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            vals[i] = d[order[i]];
            for (int r = 0; r < n; r++) vecs[r, i] = v[r, order[i]];
        }
        return (vals, vecs);
    }

    private static void Rotate(double[,] a, int p, int q, int r, int sIndex, double sgn, double tau)
    {
        double apq = a[p, q];
        double ars = a[r, sIndex];
        a[p, q] = apq - sgn * (ars + tau * apq);
        a[r, sIndex] = ars + sgn * (apq - tau * ars);
    }
}

public sealed class ThinSvd
{
    public double[] SingularValues { get; }
    public double[,] V { get; }
    public int Rank(double tol)
    {
        if (SingularValues.Length == 0) return 0;
        double top = SingularValues[0];
        int rank = 0;
        foreach (var sv in SingularValues) if (sv > tol * top) rank++;
        return rank;
    }

    public ThinSvd(double[,] matrix)
    {
        int m = matrix.GetLength(0);
        int n = matrix.GetLength(1);
        var ata = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < m; k++) sum += matrix[k, i] * matrix[k, j];
                ata[i, j] = sum;
            }
        var (eig, v) = JacobiEigen.Decompose(ata);
        SingularValues = new double[n];
        for (int i = 0; i < n; i++) SingularValues[i] = eig[i] > 0 ? System.Math.Sqrt(eig[i]) : 0;
        V = v;
    }

    // Minimum-norm unconstrained least squares solution via the thin SVD.
    // Used for diagnostics only; it is never clipped/renormalized into an answer.
    public double[] LeastSquares(double[,] a, double[] y, double tol)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        double[] aty = new double[n];
        for (int i = 0; i < n; i++)
            for (int k = 0; k < m; k++) aty[i] += a[k, i] * y[k];

        var x = new double[n];
        for (int j = 0; j < n; j++)
        {
            if (SingularValues[j] <= tol * (SingularValues[0].Or(1))) continue;
            double ut = 0;
            for (int i = 0; i < n; i++) ut += V[i, j] * aty[i];
            double coef = ut / (SingularValues[j] * SingularValues[j]);
            for (int i = 0; i < n; i++) x[i] += V[i, j] * coef;
        }
        return x;
    }
}

internal static class DoubleExt
{
    public static double Or(this double v, double fallback) => v == 0 ? fallback : v;
}

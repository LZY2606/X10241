namespace IsotopeMixing.Core.Math;

// Minimal dense matrix helpers used by the mixing solver. Cholesky-based inversion is
// sufficient because measurement covariances are symmetric positive (semi-)definite.
public sealed class Matrix
{
    public int Rows { get; }
    public int Cols { get; }
    public double[] Data { get; }
    public Matrix(int rows, int cols, double[]? data = null)
    {
        Rows = rows; Cols = cols;
        Data = data ?? new double[rows * cols];
    }
    public ref double this[int r, int c] => ref Data[r * Cols + c];

    public static Matrix Zeros(int r, int c) => new(r, c);
    public static Matrix Identity(int n)
    {
        var m = new Matrix(n, n);
        for (int i = 0; i < n; i++) m[i, i] = 1;
        return m;
    }
    public static Matrix FromRows(params double[][] rows)
    {
        int r = rows.Length, c = rows[0].Length;
        var m = new Matrix(r, c);
        for (int i = 0; i < r; i++) Array.Copy(rows[i], 0, m.Data, i * c, c);
        return m;
    }
    public double[] Row(int r)
    {
        var v = new double[Cols];
        Array.Copy(Data, r * Cols, v, 0, Cols);
        return v;
    }
    public double[] Column(int c)
    {
        var v = new double[Rows];
        for (int r = 0; r < Rows; r++) v[r] = this[r, c];
        return v;
    }
    public Matrix Clone() => new(Rows, Cols, (double[])Data.Clone());

    public static Matrix Multiply(Matrix a, Matrix b)
    {
        if (a.Cols != b.Rows) throw new ArgumentException("shape mismatch");
        var c = new Matrix(a.Rows, b.Cols);
        for (int i = 0; i < a.Rows; i++)
            for (int k = 0; k < a.Cols; k++)
            {
                double aik = a[i, k];
                if (aik == 0) continue;
                for (int j = 0; j < b.Cols; j++)
                    c[i, j] += aik * b[k, j];
            }
        return c;
    }

    public static double[] Multiply(Matrix a, double[] x)
    {
        var y = new double[a.Rows];
        for (int i = 0; i < a.Rows; i++)
        {
            double s = 0;
            for (int j = 0; j < a.Cols; j++) s += a[i, j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public Matrix Transpose()
    {
        var t = new Matrix(Cols, Rows);
        for (int i = 0; i < Rows; i++)
            for (int j = 0; j < Cols; j++) t[j, i] = this[i, j];
        return t;
    }

    // Returns lower-triangular Cholesky factor L (A = L L^T) or null if not positive definite.
    public Matrix? Cholesky()
    {
        int n = Rows;
        if (Cols != n) return null;
        var l = new Matrix(n, n);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = this[i, j];
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (sum <= 1e-14) return null;
                    l[i, j] = System.Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
        }
        return l;
    }

    public (double[]? x, int rank) SolveSymmetric(double[] b)
    {
        int n = Rows;
        var chol = Cholesky();
        if (chol is not null) return (chol.SolveLowerThenUpper(b), n);
        // Fallback: Gaussian elimination with partial pivot + rank detection.
        var aug = new double[n * (n + 1)];
        for (int i = 0; i < n; i++)
        {
            Array.Copy(Data, i * n, aug, i * (n + 1), n);
            aug[i * (n + 1) + n] = b[i];
        }
        int rank = 0;
        var pivcol = new int[n];
        for (int col = 0; col < n && rank < n; col++)
        {
            int piv = rank;
            double best = System.Math.Abs(aug[rank * (n + 1) + col]);
            for (int r = rank + 1; r < n; r++)
            {
                double v = System.Math.Abs(aug[r * (n + 1) + col]);
                if (v > best) { best = v; piv = r; }
            }
            if (best < 1e-11) continue;
            if (piv != rank)
                for (int k = 0; k <= n; k++)
                    (aug[rank * (n + 1) + k], aug[piv * (n + 1) + k]) =
                        (aug[piv * (n + 1) + k], aug[rank * (n + 1) + k]);
            pivcol[rank] = col;
            for (int r = 0; r < n; r++)
                if (r != rank)
                {
                    double f = aug[r * (n + 1) + col] / aug[rank * (n + 1) + col];
                    if (f == 0) continue;
                    for (int k = col; k <= n; k++)
                        aug[r * (n + 1) + k] -= f * aug[rank * (n + 1) + k];
                }
            rank++;
        }
        for (int r = rank; r < n; r++)
            if (System.Math.Abs(aug[r * (n + 1) + n]) > 1e-8) return (null, rank);
        var x = new double[n];
        for (int i = 0; i < rank; i++) x[pivcol[i]] = aug[i * (n + 1) + n] / aug[i * (n + 1) + pivcol[i]];
        return (x, rank);
    }

    private double[] SolveLowerThenUpper(double[] b)
    {
        int n = Rows;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = b[i];
            for (int j = 0; j < i; j++) s -= this[i, j] * y[j];
            y[i] = s / this[i, i];
        }
        // L^T x = y ; L stored here, use its transpose.
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = y[i];
            for (int j = i + 1; j < n; j++) s -= this[j, i] * x[j];
            x[i] = s / this[i, i];
        }
        return x;
    }

    public double Determinant()
    {
        var l = Cholesky();
        if (l is null) return 0;
        double d = 1;
        for (int i = 0; i < Rows; i++) d *= l[i, i];
        return d * d;
    }
}

public static class LinearAlgebra
{
    public static Matrix InverseSPD(Matrix a)
    {
        var inv = new Matrix(a.Rows, a.Cols);
        for (int j = 0; j < a.Rows; j++)
        {
            var e = new double[a.Rows];
            e[j] = 1;
            var col = a.SolveSymmetric(e).x!;
            for (int i = 0; i < a.Rows; i++) inv[i, j] = col[i];
        }
        return inv;
    }

    // Propagate covariance through a linear map: Cy = J Cx J^T.
    public static Matrix Propagate(Matrix jacobian, Matrix cov)
        => Matrix.Multiply(Matrix.Multiply(jacobian, cov), jacobian.Transpose());

    public static double ChiSquareQuantile95(int df)
    {
        // Precomputed 0.95 quantiles for the dimensions used in isotope mixing.
        return df switch
        {
            1 => 3.84146,
            2 => 5.99146,
            3 => 7.81473,
            4 => 9.48773,
            _ => WilsonHilferty(df, 0.95)
        };
    }

    private static double WilsonHilferty(int df, double p)
    {
        // Approximation fallback for unexpected dimensionality.
        double z = 1.6448536269514722; // N(0,1) 0.95 quantile
        double t = 2.0 / (9.0 * df);
        double h = System.Math.Pow(1 - t + z * System.Math.Sqrt(t), 3);
        return df * h;
    }
}

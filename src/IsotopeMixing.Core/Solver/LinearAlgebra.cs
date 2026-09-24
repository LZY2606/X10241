namespace IsotopeMixing.Core.Solver;

/// <summary>Small-matrix linear algebra used by the mixing solver (d,k are isotope/endmember counts).</summary>
public static class LinearAlgebra
{
    public static double[][] Matrix(int rows, int cols, double fill = 0)
    {
        var m = new double[rows][];
        for (var i = 0; i < rows; i++)
        {
            m[i] = new double[cols];
            System.Array.Fill(m[i], fill);
        }
        return m;
    }

    public static double[][] Copy(double[][] a) => a.Select(r => (double[])r.Clone()).ToArray();

    public static double[] Copy(double[] a) => (double[])a.Clone();

    public static double Dot(double[] a, double[] b)
    {
        var s = 0.0;
        for (var i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    public static double[] MatVec(double[][] a, double[] x)
    {
        var y = new double[a.Length];
        for (var i = 0; i < a.Length; i++) y[i] = Dot(a[i], x);
        return y;
    }

    public static double[][] Transpose(double[][] a)
    {
        if (a.Length == 0) return System.Array.Empty<double[]>();
        var t = Matrix(a[0].Length, a.Length);
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < a[0].Length; j++)
                t[j][i] = a[i][j];
        return t;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        var c = Matrix(a.Length, b[0].Length);
        for (var i = 0; i < c.Length; i++)
            for (var j = 0; j < c[0].Length; j++)
            {
                var s = 0.0;
                for (var t = 0; t < b.Length; t++) s += a[i][t] * b[t][j];
                c[i][j] = s;
            }
        return c;
    }

    /// <summary>LDL^T decomposition with symmetric pivoting. Returns null if not positive definite.</summary>
    public static (int[] Perm, double[] Diag, double[][] Lower)? LDLPivot(double[][] aIn, double pivotEps = 1e-12)
    {
        var n = aIn.Length;
        var a = Copy(aIn);
        var perm = Enumerable.Range(0, n).ToArray();
        var diag = new double[n];
        var lower = Matrix(n, n);

        for (var i = 0; i < n; i++)
        {
            var pivot = i;
            for (var r = i + 1; r < n; r++)
                if (System.Math.Abs(a[r][i]) > System.Math.Abs(a[pivot][i])) pivot = r;

            if (System.Math.Abs(a[pivot][i]) <= pivotEps) return null;

            if (pivot != i)
            {
                (a[i], a[pivot]) = (a[pivot], a[i]);
                for (var r = 0; r < n; r++) (a[r][i], a[r][pivot]) = (a[r][pivot], a[r][i]);
                (perm[i], perm[pivot]) = (perm[pivot], perm[i]);
            }

            diag[i] = a[i][i];
            if (diag[i] <= pivotEps) return null;

            for (var j = i + 1; j < n; j++)
            {
                lower[j][i] = a[j][i] / diag[i];
                for (var r = i + 1; r <= j; r++)
                {
                    var v = a[j][r] - lower[j][i] * a[r][i];
                    a[j][r] = v;
                    a[r][j] = v;
                }
            }
        }

        return (perm, diag, lower);
    }

    /// <summary>Solve SPD system via LDL^T. Returns null when not positive definite.</summary>
    public static double[]? SolveSPD(double[][] a, double[] b)
    {
        var dec = LDLPivot(a);
        if (dec is null) return null;
        var (perm, diag, lower) = dec.Value;
        var n = b.Length;
        var pb = new double[n];
        for (var i = 0; i < n; i++) pb[i] = b[perm[i]];

        var z = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = pb[i];
            for (var j = 0; j < i; j++) s -= lower[i][j] * z[j];
            z[i] = s;
        }

        for (var i = 0; i < n; i++) z[i] /= diag[i];

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var s = z[i];
            for (var j = i + 1; j < n; j++) s -= lower[j][i] * x[j];
            x[i] = s;
        }

 return x;
    }

    public static double[] SolveSquare(double[][] a, double[] b)
    {
        var n = a.Length;
        var m = new double[n][];
        for (var i = 0; i < n; i++)
        {
            m[i] = new double[n + 1];
            System.Array.Copy(a[i], m[i], n);
            m[i][n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
                if (System.Math.Abs(m[r][col]) > System.Math.Abs(m[pivot][col])) pivot = r;
            (m[col], m[pivot]) = (m[pivot], m[col]);
            if (System.Math.Abs(m[col][col]) < 1e-14)
                throw new InvalidOperationException("singular system");

            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = m[r][col] / m[col][col];
                for (var c = col; c <= n; c++) m[r][c] -= f * m[col][c];
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++) x[i] = m[i][n] / m[i][i];
        return x;
    }

    /// <summary>Singular values of A from eigenvalues of A^T A via cyclic Jacobi sweeps.</summary>
    public static double[] SingularValues(double[][] a)
    {
        if (a.Length == 0 || a[0].Length == 0) return System.Array.Empty<double>();
        var ata = Multiply(Transpose(a), a);
        var n = ata.Length;
        for (var sweep = 0; sweep < 100; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                    off += ata[p][q] * ata[p][q];
            if (off < 1e-26) break;

            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (System.Math.Abs(ata[p][q]) < 1e-15) continue;
                var tau = (ata[q][q] - ata[p][p]) / (2.0 * ata[p][q]);
                var t = (tau >= 0 ? 1 : -1) / (System.Math.Abs(tau) + System.Math.Sqrt(1.0 + tau * tau));
                var c = 1.0 / System.Math.Sqrt(1.0 + t * t);
                var s = t * c;

                for (var r = 0; r < n; r++)
                {
                    var arp = ata[r][p];
                    var arq = ata[r][q];
                    ata[r][p] = c * arp - s * arq;
                    ata[p][r] = ata[r][p];
                    ata[r][q] = s * arp + c * arq;
                    ata[q][r] = ata[r][q];
                }
            }
        }

        var sv = new double[n];
        for (var i = 0; i < n; i++) sv[i] = System.Math.Sqrt(System.Math.Max(0, ata[i][i]));
        System.Array.Sort(sv);
        System.Array.Reverse(sv);
        return sv;
    }

    /// <summary>Chi-square 0.95 quantiles for degrees of freedom 1..8.</summary>
    public static double ChiSquare95(int df)
    {
        double[] t = { 3.8415, 5.9915, 7.8147, 9.4877, 11.0705, 12.5916, 14.0671, 15.5073 };
        return t[System.Math.Clamp(df, 1, 8) - 1];
    }
}

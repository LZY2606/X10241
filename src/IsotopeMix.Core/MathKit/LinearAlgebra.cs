namespace IsotopeMix.Core.MathKit;

/// <summary>Small dense matrix helpers for the mixing solver (dimensions are isotope tracers, 2..6).</summary>
public static class LinearAlgebra
{
    public static double[][] Matrix(int rows, int cols, double fill = 0)
    {
        var m = new double[rows][];
        for (var i = 0; i < rows; i++)
        {
            m[i] = new double[cols];
            Array.Fill(m[i], fill);
        }
        return m;
    }

    public static double[] Solve(double[][] A, double[] b)
    {
        var n = A.Length;
        var aug = new double[n][];
        for (var i = 0; i < n; i++)
        {
            aug[i] = new double[n + 1];
            Array.Copy(A[i], aug[i], n);
            aug[i][n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[pivot][col])) pivot = r;
            if (Math.Abs(aug[pivot][col]) < 1e-12)
                throw new SingularMatrixException();
            (aug[pivot], aug[col]) = (aug[col], aug[pivot]);
            var pv = aug[col][col];
            for (var j = col; j <= n; j++) aug[col][j] /= pv;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = aug[r][col];
                if (f == 0) continue;
                for (var j = col; j <= n; j++) aug[r][j] -= f * aug[col][j];
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++) x[i] = aug[i][n];
        return x;
    }

    public sealed class SingularMatrixException : Exception;

    /// <summary>Solves X A = B for symmetric systems via Gaussian elimination; returns null when singular.</summary>
    public static double[]? TrySolve(double[][] A, double[] b)
    {
        try { return Solve(A, b); }
        catch (SingularMatrixException) { return null; }
    }

    public static double[][] Multiply(double[][] A, double[][] B)
    {
        var n = A.Length;
        var m = B[0].Length;
        var k = B.Length;
        var c = Matrix(n, m);
        for (var i = 0; i < n; i++)
        for (var j = 0; j < m; j++)
        {
            var s = 0.0;
            for (var t = 0; t < k; t++) s += A[i][t] * B[t][j];
            c[i][j] = s;
        }
        return c;
    }

    public static double[] Multiply(double[][] A, double[] x)
    {
        var y = new double[A.Length];
        for (var i = 0; i < A.Length; i++)
        {
            var s = 0.0;
            for (var j = 0; j < x.Length; j++) s += A[i][j] * x[j];
            y[i] = s;
        }
        return y;
    }

    public static double[][] Transpose(double[][] A)
    {
        var t = Matrix(A[0].Length, A.Length);
        for (var i = 0; i < A.Length; i++)
        for (var j = 0; j < A[0].Length; j++)
            t[j][i] = A[i][j];
        return t;
    }

    /// <summary>Inverse of a small matrix; null when singular.</summary>
    public static double[][]? Inverse(double[][] A)
    {
        var n = A.Length;
        var inv = Matrix(n, n);
        for (var col = 0; col < n; col++)
        {
            var e = new double[n];
            e[col] = 1;
            var x = TrySolve(A, e);
            if (x is null) return null;
            for (var i = 0; i < n; i++) inv[i][col] = x[i];
        }
        return inv;
    }

    /// <summary>Eigenvalues of a real symmetric matrix via cyclic Jacobi rotations, descending order.</summary>
    public static double[] SymmetricEigenvalues(double[][] S)
    {
        var n = S.Length;
        var a = S.Select(r => (double[])r.Clone()).ToArray();
        for (var sweep = 0; sweep < 100; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
                off += a[p][q] * a[p][q];
            if (off < 1e-28) break;

            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (Math.Abs(a[p][q]) < 1e-15) continue;
                var tau = (a[q][q] - a[p][p]) / (2 * a[p][q]);
                var t = Math.Sign(tau == 0 ? 1 : tau) /
                        (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                var c = 1 / Math.Sqrt(1 + t * t);
                var s = t * c;

                for (var k = 0; k < n; k++)
                {
                    var akp = a[k][p];
                    var akq = a[k][q];
                    a[k][p] = c * akp - s * akq;
                    a[k][q] = s * akp + c * akq;
                }
                for (var k = 0; k < n; k++)
                {
                    var apk = a[p][k];
                    var aqk = a[q][k];
                    a[p][k] = c * apk - s * aqk;
                    a[q][k] = s * apk + c * aqk;
                }
            }
        }

        var vals = new double[n];
        for (var i = 0; i < n; i++) vals[i] = a[i][i];
        Array.Sort(vals, (x, y) => y.CompareTo(x));
        return vals;
    }

    public static bool IsPositiveDefinite(double[][] S)
    {
        var eigs = SymmetricEigenvalues(S);
        var scale = eigs.Length == 0 ? 0 : eigs[0];
        return eigs[^1] > 1e-12 * Math.Max(1, scale);
    }

    /// <summary>
    /// 2-norm condition number of A via eigenvalues of AᵀA. Returns infinity when rank deficient.
    /// </summary>
    public static double ConditionNumber(double[][] A)
    {
        var ata = Multiply(Transpose(A), A);
        var eigs = SymmetricEigenvalues(ata).Select(v => Math.Max(0, v)).ToArray();
        if (eigs[^1] <= 1e-14 * Math.Max(1, eigs[0])) return double.PositiveInfinity;
        return Math.Sqrt(eigs[0] / eigs[^1]);
    }
}

namespace IsoMix.Engine;

/// <summary>Small dense-matrix helpers for the mixing engine (d <= 4, k <= 4).</summary>
public static class LinAlg
{
    public static double[][] Identity(int n)
    {
        var m = new double[n][];
        for (var i = 0; i < n; i++) { m[i] = new double[n]; m[i][i] = 1.0; }
        return m;
    }

    public static double[][] Multiply(double[][] a, double[][] b)
    {
        var rows = a.Length; var inner = b.Length; var cols = b[0].Length;
        var r = new double[rows][];
        for (var i = 0; i < rows; i++)
        {
            r[i] = new double[cols];
            for (var j = 0; j < cols; j++)
                for (var k = 0; k < inner; k++)
                    r[i][j] += a[i][k] * b[k][j];
        }
        return r;
    }

    public static double[] Multiply(double[][] a, double[] x)
    {
        var r = new double[a.Length];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < x.Length; j++)
                r[i] += a[i][j] * x[j];
        return r;
    }

    public static double[][] Transpose(double[][] a)
    {
        var r = new double[a[0].Length][];
        for (var j = 0; j < r.Length; j++)
        {
            r[j] = new double[a.Length];
            for (var i = 0; i < a.Length; i++) r[j][i] = a[i][j];
        }
        return r;
    }

    /// <summary>Cholesky factor L of a symmetric positive-definite matrix (a = L Lᵀ).</summary>
    public static double[][] Cholesky(double[][] a)
    {
        var n = a.Length;
        var l = new double[n][];
        for (var i = 0; i < n; i++) l[i] = new double[n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j <= i; j++)
            {
                var sum = a[i][j];
                for (var k = 0; k < j; k++) sum -= l[i][k] * l[j][k];
                if (i == j)
                {
                    if (sum <= 0) throw new InvalidOperationException("covariance matrix is not positive definite");
                    l[i][j] = Math.Sqrt(sum);
                }
                else l[i][j] = sum / l[j][j];
            }
        return l;
    }

    /// <summary>Solve L x = b for lower-triangular L (forward substitution).</summary>
    public static double[] ForwardSolve(double[][] l, double[] b)
    {
        var n = l.Length;
        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var k = 0; k < i; k++) sum -= l[i][k] * x[k];
            x[i] = sum / l[i][i];
        }
        return x;
    }

    /// <summary>Whiten: returns L⁻¹·a for a (n×m) matrix, given Cholesky L of the covariance.</summary>
    public static double[][] ForwardSolve(double[][] l, double[][] a)
    {
        var cols = a[0].Length;
        var result = new double[a.Length][];
        for (var j = 0; j < cols; j++)
        {
            var col = new double[a.Length];
            for (var i = 0; i < a.Length; i++) col[i] = a[i][j];
            var solved = ForwardSolve(l, col);
            for (var i = 0; i < a.Length; i++)
            {
                result[i] ??= new double[cols];
                result[i][j] = solved[i];
            }
        }
        return result;
    }

    /// <summary>Jacobi eigenvalue decomposition of a symmetric matrix. Returns eigenvalues ascending and eigenvectors as columns.</summary>
    public static (double[] Values, double[][] Vectors) JacobiEigen(double[][] a, int maxSweeps = 100, double tol = 1e-12)
    {
        var n = a.Length;
        var m = a.Select(row => row.ToArray()).ToArray();
        var v = Identity(n);
        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++) for (var q = p + 1; q < n; q++) off += m[p][q] * m[p][q];
            if (Math.Sqrt(off) < tol) break;
            for (var p = 0; p < n - 1; p++)
                for (var q = p + 1; q < n; q++)
                {
                    if (Math.Abs(m[p][q]) < 1e-300) continue;
                    var theta = 0.5 * (m[q][q] - m[p][p]) / m[p][q];
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;
                    for (var k = 0; k < n; k++)
                    {
                        var mkp = m[k][p]; var mkq = m[k][q];
                        m[k][p] = c * mkp - s * mkq;
                        m[k][q] = s * mkp + c * mkq;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var mpk = m[p][k]; var mqk = m[q][k];
                        m[p][k] = c * mpk - s * mqk;
                        m[q][k] = s * mpk + c * mqk;
                    }
                    for (var k = 0; k < n; k++)
                    {
                        var vkp = v[k][p]; var vkq = v[k][q];
                        v[k][p] = c * vkp - s * vkq;
                        v[k][q] = s * vkp + c * vkq;
                    }
                }
        }
        var values = new double[n];
        for (var i = 0; i < n; i++) values[i] = m[i][i];
        var order = values.Select((val, idx) => (val, idx)).OrderBy(t => t.val).Select(t => t.idx).ToArray();
        var sortedValues = order.Select(i => values[i]).ToArray();
        var sortedVectors = new double[n][];
        for (var r = 0; r < n; r++)
        {
            sortedVectors[r] = new double[n];
            for (var c = 0; c < n; c++) sortedVectors[r][c] = v[r][order[c]];
        }
        return (sortedValues, sortedVectors);
    }

    public static double[][] SubMatrix(double[][] a, int[] rowIdx, int[] colIdx)
    {
        var r = new double[rowIdx.Length][];
        for (var i = 0; i < rowIdx.Length; i++)
        {
            r[i] = new double[colIdx.Length];
            for (var j = 0; j < colIdx.Length; j++) r[i][j] = a[rowIdx[i]][colIdx[j]];
        }
        return r;
    }

    public static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
}

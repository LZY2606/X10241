namespace IsoLab.Core;

/// <summary>Small dense linear-algebra helpers (no external deps).</summary>
public static class LinAlg
{
    public static double[][] Identity(int n)
    {
        var m = new double[n][];
        for (var i = 0; i < n; i++) { m[i] = new double[n]; m[i][i] = 1.0; }
        return m;
    }

    /// <summary>Gauss-Jordan inverse with partial pivoting. Throws if singular.</summary>
    public static double[][] Inverse(double[][] a)
    {
        var n = a.Length;
        var aug = new double[n][];
        for (var i = 0; i < n; i++)
        {
            aug[i] = new double[2 * n];
            for (var j = 0; j < n; j++) aug[i][j] = a[i][j];
            aug[i][n + i] = 1.0;
        }
        for (var col = 0; col < n; col++)
        {
            var piv = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(aug[r][col]) > Math.Abs(aug[piv][col])) piv = r;
            if (Math.Abs(aug[piv][col]) < 1e-14) throw new InvalidOperationException("singular matrix");
            (aug[col], aug[piv]) = (aug[piv], aug[col]);
            var d = aug[col][col];
            for (var j = 0; j < 2 * n; j++) aug[col][j] /= d;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = aug[r][col];
                if (f == 0) continue;
                for (var j = 0; j < 2 * n; j++) aug[r][j] -= f * aug[col][j];
            }
        }
        var inv = new double[n][];
        for (var i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            for (var j = 0; j < n; j++) inv[i][j] = aug[i][n + j];
        }
        return inv;
    }

    /// <summary>Jacobi eigenvalues of a symmetric matrix (descending).</summary>
    public static double[] SymmetricEigenvalues(double[][] a)
    {
        var n = a.Length;
        var m = a.Select(r => (double[])r.Clone()).ToArray();
        for (var sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (var p = 0; p < n; p++) for (var q = p + 1; q < n; q++) off += m[p][q] * m[p][q];
            if (Math.Sqrt(off) < 1e-12) break;
            for (var p = 0; p < n - 1; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (Math.Abs(m[p][q]) < 1e-15) continue;
                var theta = 0.5 * Math.Atan2(2 * m[p][q], m[q][q] - m[p][p]);
                var c = Math.Cos(theta); var s = Math.Sin(theta);
                for (var k = 0; k < n; k++) { var mkp = m[k][p]; var mkq = m[k][q]; m[k][p] = c * mkp - s * mkq; m[k][q] = s * mkp + c * mkq; }
                for (var k = 0; k < n; k++) { var mpk = m[p][k]; var mqk = m[q][k]; m[p][k] = c * mpk - s * mqk; m[q][k] = s * mpk + c * mqk; }
            }
        }
        var ev = new double[n];
        for (var i = 0; i < n; i++) ev[i] = m[i][i];
        Array.Sort(ev); Array.Reverse(ev);
        return ev;
    }

    /// <summary>Condition number (2-norm) of a rectangular matrix via eigenvalues of AᵀA.</summary>
    public static double ConditionNumber(double[][] a)
    {
        var rows = a.Length; var cols = a[0].Length;
        var ata = new double[cols][];
        for (var i = 0; i < cols; i++)
        {
            ata[i] = new double[cols];
            for (var j = 0; j < cols; j++)
            {
                double s = 0;
                for (var r = 0; r < rows; r++) s += a[r][i] * a[r][j];
                ata[i][j] = s;
            }
        }
        var ev = SymmetricEigenvalues(ata);
        var max = ev[0];
        var min = ev[^1];
        if (min <= 1e-20) return double.PositiveInfinity;
        return Math.Sqrt(max / min);
    }

    /// <summary>Solve min ||Ap - b|| subject to sum(p) = 1 (equality-constrained LS via KKT).</summary>
    public static double[] EqualityConstrainedLeastSquares(double[][] a, double[] b)
    {
        var rows = a.Length; var k = a[0].Length;
        var kkt = new double[k + 1][];
        for (var i = 0; i < k + 1; i++) kkt[i] = new double[k + 1];
        var rhs = new double[k + 1];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++)
            {
                double s = 0;
                for (var r = 0; r < rows; r++) s += a[r][i] * a[r][j];
                kkt[i][j] = s;
            }
            double sb = 0;
            for (var r = 0; r < rows; r++) sb += a[r][i] * b[r];
            rhs[i] = sb;
            kkt[i][k] = 1.0; kkt[k][i] = 1.0;
        }
        rhs[k] = 1.0;
        var inv = Inverse(kkt);
        var p = new double[k];
        for (var i = 0; i < k; i++)
        {
            double s = 0;
            for (var j = 0; j <= k; j++) s += inv[i][j] * rhs[j];
            p[i] = s;
        }
        return p;
    }
}

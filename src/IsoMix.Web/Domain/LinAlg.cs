namespace IsoMix.Domain;

/// <summary>小规模稠密线性代数（引擎内部使用，维度 ≤ 8）。</summary>
public static class LinAlg
{
    public static double[][] Invert(double[][] a)
    {
        int n = a.Length;
        var m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[2 * n];
            for (int j = 0; j < n; j++) m[i][j] = a[i][j];
            m[i][n + i] = 1.0;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r][col]) > Math.Abs(m[piv][col])) piv = r;
            if (Math.Abs(m[piv][col]) < 1e-14)
                throw new InvalidOperationException("矩阵奇异，无法求逆");
            (m[col], m[piv]) = (m[piv], m[col]);
            double d = m[col][col];
            for (int j = 0; j < 2 * n; j++) m[col][j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r][col];
                for (int j = 0; j < 2 * n; j++) m[r][j] -= f * m[col][j];
            }
        }
        var inv = new double[n][];
        for (int i = 0; i < n; i++)
        {
            inv[i] = new double[n];
            Array.Copy(m[i], n, inv[i], 0, n);
        }
        return inv;
    }

    /// <summary>解线性方程组 Ax = b（高斯消元，部分主元）。</summary>
    public static double[] Solve(double[][] a, double[] b)
    {
        int n = a.Length;
        var m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[n + 1];
            Array.Copy(a[i], m[i], n);
            m[i][n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r][col]) > Math.Abs(m[piv][col])) piv = r;
            if (Math.Abs(m[piv][col]) < 1e-14)
                throw new InvalidOperationException("矩阵奇异，无法求解");
            (m[col], m[piv]) = (m[piv], m[col]);
            for (int r = col + 1; r < n; r++)
            {
                double f = m[r][col] / m[col][col];
                for (int j = col; j <= n; j++) m[r][j] -= f * m[col][j];
            }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = m[i][n];
            for (int j = i + 1; j < n; j++) s -= m[i][j] * x[j];
            x[i] = s / m[i][i];
        }
        return x;
    }

    /// <summary>对称矩阵特征值（Jacobi 迭代），用于条件数。</summary>
    public static double[] EigenvaluesSymmetric(double[][] a)
    {
        int n = a.Length;
        var m = a.Select(r => (double[])r.Clone()).ToArray();
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                    off += m[p][q] * m[p][q];
            if (off < 1e-20) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(m[p][q]) < 1e-15) continue;
                    double theta = 0.5 * Math.Atan2(2 * m[p][q], m[q][q] - m[p][p]);
                    double c = Math.Cos(theta), s = Math.Sin(theta);
                    for (int k = 0; k < n; k++)
                    {
                        double mkp = m[k][p], mkq = m[k][q];
                        m[k][p] = c * mkp - s * mkq;
                        m[k][q] = s * mkp + c * mkq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double mpk = m[p][k], mqk = m[q][k];
                        m[p][k] = c * mpk - s * mqk;
                        m[q][k] = s * mpk + c * mqk;
                    }
                }
        }
        return Enumerable.Range(0, n).Select(i => m[i][i]).ToArray();
    }
}

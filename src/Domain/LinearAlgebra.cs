namespace IsotopeMix.Domain;

/// <summary>
/// 仅依赖 BLAS 之外不可用时的小型稠密线性代数：
/// 对称矩阵 Jacobi 特征分解（兼作奇异值与协方差求逆）。
/// </summary>
public static class LinearAlgebra
{
    public sealed class SymmetricEigen
    {
        public double[] Values { get; init; } = Array.Empty<double>();
        public double[,] Vectors { get; init; } = { };
    }

    public static SymmetricEigen JacobiEigen(double[,] aIn, int maxSweeps = 100)
    {
        var n = aIn.GetLength(0);
        var a = (double[,])aIn.Clone();
        var v = new double[n, n];
        for (var i = 0; i < n; i++) v[i, i] = 1.0;

        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < n; p++)
                for (var q = p + 1; q < n; q++)
                    off += a[p, q] * a[p, q];
            if (off < 1e-30) break;

            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                var apq = a[p, q];
                if (Math.Abs(apq) < 1e-300) continue;
                var app = a[p, p];
                var aqq = a[q, q];
                var phi = Math.Atan2(2 * apq, aqq - app) / 2.0;
                var c = Math.Cos(phi);
                var s = Math.Sin(phi);

                for (var k = 0; k < n; k++)
                {
                    var akp = a[k, p];
                    var akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (var k = 0; k < n; k++)
                {
                    var apk = a[p, k];
                    var aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
                for (var k = 0; k < n; k++)
                {
                    var vkp = v[k, p];
                    var vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
        }

        var vals = new double[n];
        for (var i = 0; i < n; i++) vals[i] = a[i, i];
        // 特征值降序排列，并同步重排特征向量列，保持配对。
        var order = Enumerable.Range(0, n).OrderByDescending(i => vals[i]).ToArray();
        var sortedVals = order.Select(i => vals[i]).ToArray();
        var sortedV = new double[n, n];
        for (var newI = 0; newI < n; newI++)
        for (var r = 0; r < n; r++)
            sortedV[r, newI] = v[r, order[newI]];
        return new SymmetricEigen { Values = sortedVals, Vectors = sortedV };
    }

    /// <summary>B 的奇异值（B = U S V'），经 B'B 的特征值得到，降序。</summary>
    public static double[] SingularValues(double[,] b)
    {
        var rows = b.GetLength(0);
        var cols = b.GetLength(1);
        var btb = new double[cols, cols];
        for (var i = 0; i < cols; i++)
        for (var j = 0; j < cols; j++)
        {
            var sum = 0.0;
            for (var r = 0; r < rows; r++) sum += b[r, i] * b[r, j];
            btb[i, j] = sum;
        }
        var eig = JacobiEigen(btb);
        return eig.Values.Select(x => x <= 0 ? 0 : Math.Sqrt(x)).ToArray();
    }

    /// <summary>对称正定（或加底后）矩阵的逆与马氏距离二次型。</summary>
    public static double[,] SymmetricInverse(double[,] m, double eigenFloor)
    {
        var n = m.GetLength(0);
        var eig = JacobiEigen(m);
        var v = eig.Vectors;
        var inv = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            var lambda = Math.Max(eig.Values[i], eigenFloor);
            for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++)
                    inv[r, c] += v[r, i] * v[c, i] / lambda;
        }
        return inv;
    }

    /// <summary>Wilson–Hilferty 卡方上分位数近似（df 自由度）。</summary>
    public static double ChiSquareUpperQuantile(int df, double z)
    {
        var h = 2.0 / (9.0 * df);
        var cubed = 1.0 - h + z * Math.Sqrt(h);
        var x = df * cubed * cubed * cubed;
        return Math.Max(x, double.Epsilon);
    }
}

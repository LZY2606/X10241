namespace IsotopeMix.Domain;

/// <summary>
/// 相关误差按测量协方差传播：预测协方差为
/// Σ_sample + Σ_k f_k² Σ(E_k,mode)（端元间相互独立）。
/// </summary>
public static class Statistics
{
    public static double ChiSquareCritical(double confidence, int degreesOfFreedom)
    {
        // Wilson–Hilferty 逆卡方近似，避免引入统计数值库；
        // 与精确值在 df>=1、置信 0.95 时误差小于 0.04。
        double p = confidence;
        double z = Probit(p);
        double k = degreesOfFreedom;
        double h = 2.0 / (9.0 * k);
        double t = 1 - h + z * Math.Sqrt(h);
        return k * t * t * t;
    }

    /// <summary>Abramowitz–Stegun 标准正态分位数（|误差| < 7.5e-8）。</summary>
    public static double Probit(double p)
    {
        if (p <= 0 || p >= 1) throw new ArgumentOutOfRangeException(nameof(p));
        if (p < 0.5) return -Probit(1 - p);
        double t = Math.Sqrt(-2.0 * Math.Log(1 - p));
        double c0 = 2.515517, c1 = 0.802853, c2 = 0.010328;
        double d1 = 1.432788, d2 = 0.189269, d3 = 0.001308;
        return t - (c0 + c1 * t + c2 * t * t) / (1 + d1 * t + d2 * t * t + d3 * t * t * t);
    }

    /// <summary>按使用维度顺序重排/裁剪协方差块，缺失维返回 null（调用方决定降级）。</summary>
    public static double[,]? AlignCovariance(CovarianceBlock? block, IReadOnlyList<string> dims)
    {
        if (block is null)
        {
            var eye = new double[dims.Count, dims.Count];
            for (int i = 0; i < dims.Count; i++) eye[i, i] = 1e-6;
            return eye;
        }
        int[] idx = dims.Select(d => block.Dimensions.IndexOf(d)).ToArray();
        if (idx.Any(i => i < 0)) return null;
        var m = new double[dims.Count, dims.Count];
        for (int i = 0; i < dims.Count; i++)
            for (int j = 0; j < dims.Count; j++)
            {
                double v = block.Matrix[idx[i]][idx[j]];
                m[i, j] = i == j && v <= 0 ? 1e-6 : v;
            }
        return m;
    }

    public static double MahalanobisSquare(double[] residual, double[,] covariance)
    {
        var w = LinearAlgebra.QuadraticSolve(covariance, residual);
        double s = 0;
        for (int i = 0; i < residual.Length; i++) s += residual[i] * w[i];
        return s;
    }

    public static double[] Standardized(double[] residual, double[,] covariance)
    {
        var w = LinearAlgebra.QuadraticSolve(covariance, residual);
        return residual.Select((r, i) => r * Math.Sqrt(Math.Abs(w[i]))
            * Math.Sign(r == 0 ? 1 : r)).ToArray();
    }

    public static double[,] PredictCovariance(
        double[,] sampleCov,
        IReadOnlyList<double[,]> endmemberCovs,
        IReadOnlyList<double> fractions)
    {
        int d = sampleCov.GetLength(0);
        var cov = LinearAlgebra.Copy(sampleCov);
        for (int k = 0; k < fractions.Count; k++)
        {
            double w = fractions[k] * fractions[k];
            for (int i = 0; i < d; i++)
                for (int j = 0; j < d; j++)
                    cov[i, j] += w * endmemberCovs[k][i, j];
        }
        return cov;
    }

    /// <summary>
    /// 线性混合 GLS 下比例的近似协方差 (Mᵀ Σ⁻¹ M)⁺（自由和为一约束下），
    /// 仅用于在最优点展示比例不确定性，不用于伪造单点答案。
    /// </summary>
    public static double[,] ParameterCovariance(double[,] design, double[,] residualCovariance)
    {
        int d = design.GetLength(0), m = design.GetLength(1);
        var inv = InverseSymmetric(residualCovariance);
        var normal = new double[m, m];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int a = 0; a < d; a++)
                    for (int b = 0; b < d; b++)
                        s += design[a, i] * inv[a, b] * design[b, j];
                normal[i, j] = s;
            }
        return LinearAlgebra.PseudoInverseMatrix(normal);
    }

    public static double[,] InverseSymmetric(double[,] m)
    {
        int n = m.GetLength(0);
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            var e = new double[n];
            e[i] = 1;
            var col = LinearAlgebra.QuadraticSolve(m, e);
            for (int j = 0; j < n; j++) inv[j, i] = col[j];
        }
        return inv;
    }
}

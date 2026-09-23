using MathNet.Numerics.LinearAlgebra;

namespace IsotopeMix.Domain;

/// <summary>
/// 在单纯形 { p >= 0, sum p = 1 } 与可选先验硬边界 [lo,hi] 上
/// 最小化马氏距离平方 D²(p) = (A p - y)' W (A p - y)。
///
/// 这是一个带等式与不等式约束的严格凸 QP，用活动集枚举精确求解，
/// 绝不做“先无约束求解→截断负值→再归一化”的伪造处理。
/// </summary>
public static class ConstrainedWls
{
    public sealed record Result(double[] Proportions, double Objective, bool Feasible, bool AtBoundary);

    public static Result Solve(double[,] aFull, double[] yFull, double[,] wFull,
        bool[] activeDims, int nEndmembers, double lo = 0.0, double hi = 1.0)
    {
        var d = activeDims.Count(x => x);
        if (d == 0) return new Result(Unif(nEndmembers), double.PositiveInfinity, false, false);
        var (ai, yi, wi) = Slice(aFull, yFull, wFull, activeDims, nEndmembers);

        // 约束（p 空间）：lo <= p_k <= hi，sum p_k = 1
        var n = nEndmembers;
        if (lo * n > 1 + 1e-9 || hi * n < 1 - 1e-9 || lo < -1e-12 || hi > 1 + 1e-12 || hi < lo)
            return new Result(Unif(n), double.PositiveInfinity, false, false);

        if (n == 1)
        {
            var A0 = Matrix<double>.Build.DenseOfArray(ai);
            var W0 = Matrix<double>.Build.DenseOfArray(wi);
            var y0 = Vector<double>.Build.DenseOfArray(yi);
            if (lo <= 1 && hi >= 1)
                return new Result(new[] { 1.0 }, (A0.Column(0) - y0).DotProduct(W0 * (A0.Column(0) - y0)), true, false);
            return new Result(new[] { 1.0 }, double.PositiveInfinity, false, false);
        }

        var A = Matrix<double>.Build.DenseOfArray(ai);
        var W = Matrix<double>.Build.DenseOfArray(wi);
        var y = Vector<double>.Build.DenseOfArray(yi);
        var Hm = A.TransposeThisAndOtherDenseProduct(W, A); // n x n（半正定）
        // 脊项保证正定（数值稳定；不改变有解问题的解，仅固定无信息方向）
        Hm += Matrix<double>.Build.DenseIdentity(n) * 1e-10;
        var fv = -A.TransposeThisAndOtherDenseProduct(W, y); // 线性项（D² = p'Hp + 2f'p + const）

        // 化为 q = p[0..n-2]，p[n-1] = 1 - sum q
        // D² = q'Hq + 2g'q + c
        var H = Matrix<double>.Build.Dense(n - 1, n - 1);
        var g = Vector<double>.Build.Dense(n - 1);
        for (var i = 0; i < n - 1; i++)
        for (var j = 0; j < n - 1; j++)
            H[i, j] = Hm[i, j] - Hm[i, n - 1] - Hm[n - 1, j] + Hm[n - 1, n - 1];
        // D²(q) = q'Hq + 2 g'q + const
        //   2 g_i = 2 f_i - 2 f_{n-1} + 2(H_{n-1,n-1} - Σ_{j<n-1} H_{n-1,j})
        for (var i = 0; i < n - 1; i++)
            g[i] = fv[i] - fv[n - 1] + Hm[n - 1, n - 1] - SumCol(Hm, n - 1, n - 1);

        // q 空间线性约束 M q <= rhs
        //   q_k >= lo         -> -q_k <= -lo
        //   q_k <= hi -? p_k=q_k => q_k <= hi
        //   p_{n-1}=1-Σq >= lo -> Σ q_k <= 1-lo
        //   p_{n-1} <= hi        -> -Σ q_k <= hi-1
        var m = n;
        var M = Matrix<double>.Build.Dense(2 * m, n - 1);
        var rhs = new double[2 * m];
        for (var k = 0; k < n - 1; k++)
        {
            M[2 * k, k] = -1; rhs[2 * k] = -lo;
            M[2 * k + 1, k] = 1; rhs[2 * k + 1] = hi;
        }
        var rowLo = 2 * (n - 1);
        for (var k = 0; k < n - 1; k++) { M[rowLo, k] = 1; M[rowLo + 1, k] = -1; }
        rhs[rowLo] = 1 - lo; rhs[rowLo + 1] = hi - 1;

        var best = Unif(n);
        var bestObj = double.PositiveInfinity;
        var boundary = false;

        // 中心可行初值
        var center = new double[n - 1];
        var pCenter = ProjectFeasible(Unif(n), lo, hi);
        for (var k = 0; k < n - 1; k++) center[k] = pCenter[k];

        // 活动集枚举：对每个约束子集求解等式 Hq + g + M_A'λ = 0, M_A q = rhs_A
        var conCount = 2 * m;
        var subsets = Subsets(conCount, Math.Min(conCount, n - 1));
        foreach (var active in subsets)
        {
            if (active.Count == 0)
            {
                var qu = H.Solve(-g);
                if (FeasibleQ(qu, M, rhs) && EvalQ(qu, H, g) < bestObj)
                { bestObj = EvalQ(qu, H, g); best = FromQ(qu); boundary = false; }
                continue;
            }
            var na = active.Count;
            var K = Matrix<double>.Build.Dense((n - 1) + na, (n - 1) + na);
            var b = Vector<double>.Build.Dense((n - 1) + na);
            K.SetSubMatrix(0, 0, H);
            // 活动约束写为 -M_A q = -rhs_A，使 KKT: Hq + g - M_A'λ = 0, -M_A q = -rhs_A, λ >= 0
            for (var t = 0; t < na; t++)
            {
                var r = active[t];
                for (var k = 0; k < n - 1; k++) { K[k, (n - 1) + t] = -M[r, k]; K[(n - 1) + t, k] = -M[r, k]; }
                b[(n - 1) + t] = -rhs[r];
            }
            for (var k = 0; k < n - 1; k++) b[k] = -g[k];
            var sol = K.Solve(b);
            var q = sol.Take(n - 1).ToArray();
            var lambda = sol.Skip(n - 1).ToArray();
            if (!FeasibleQ(q, M, rhs)) continue;
            // λ >= 0（约束形式 Mq <= rhs，乘子非负）
            if (lambda.Any(l => l < -1e-7)) continue;
            var obj = EvalQ(q, H, g);
            if (obj < bestObj - 1e-12) { bestObj = obj; best = FromQ(q); boundary = true; }
        }

        // 后备：活动集迭代（枚举在数值退化失败时使用）
        if (double.IsPositiveInfinity(bestObj))
        {
            var p = ActiveSetFallback(ai, yi, wi, n, lo, hi);
            if (p is not null)
            {
                best = p; bestObj = ObjectiveP(A, W, y, p); boundary = true;
            }
        }

        var s = best.Sum();
        for (var k = 0; k < n; k++) best[k] = Math.Clamp(best[k], lo, hi);
        var s2 = best.Sum();
        if (Math.Abs(s2 - 1) > 1e-9)
        {
            // 数值校正（非截断归一化：仅在已满足非负/边界后做和为一的微调）
            for (var k = 0; k < n; k++) best[k] += (1 - s2) / n;
        }
        return new Result(best, ObjectiveP(A, W, y, best), true, boundary);
    }

    private static double SumCol(Matrix<double> m, int col, int skip)
    {
        double s = 0;
        for (var i = 0; i < m.RowCount; i++) if (i != skip) s += m[i, col];
        return s;
    }

    private static double EvalQ(double[] q, Matrix<double> H, Vector<double> g)
    {
        var qv = Vector<double>.Build.DenseOfArray(q);
        return qv.DotProduct(H * qv) + 2 * qv.DotProduct(g);
    }

    private static double ObjectiveP(Matrix<double> A, Matrix<double> W, Vector<double> y, double[] p)
    {
        var r = A * Vector<double>.Build.DenseOfArray(p) - y;
        return r.DotProduct(W * r);
    }

    private static double[] FromQ(double[] q)
    {
        var p = new double[q.Length + 1];
        double sum = 0;
        for (var k = 0; k < q.Length; k++) { p[k] = q[k]; sum += q[k]; }
        p[q.Length] = 1 - sum;
        return p;
    }

    private static bool FeasibleQ(double[] q, Matrix<double> M, double[] rhs)
    {
        if (q.Any(double.IsNaN) || q.Any(double.IsInfinity)) return false;
        for (var r = 0; r < M.RowCount; r++)
        {
            double v = 0;
            for (var k = 0; k < q.Length; k++) v += M[r, k] * q[k];
            if (v > rhs[r] + 1e-7) return false;
        }
        return true;
    }

    private static double[] Unif(int n) => Enumerable.Repeat(1.0 / n, n).ToArray();

    private static double[] ProjectFeasible(double[] p0, double lo, double hi)
    {
        var p = (double[])p0.Clone();
        for (var iter = 0; iter < 200; iter++)
        {
            for (var k = 0; k < p.Length; k++) p[k] = Math.Clamp(p[k], lo, hi);
            var s = p.Sum();
            if (Math.Abs(s - 1) < 1e-10) break;
            var delta = (1 - s) / p.Length;
            for (var k = 0; k < p.Length; k++) p[k] += delta;
        }
        return p;
    }

    private static IEnumerable<List<int>> Subsets(int count, int maxSize)
    {
        yield return new List<int>();
        for (var size = 1; size <= maxSize; size++)
        {
            var idx = new int[size];
            for (var i = 0; i < size; i++) idx[i] = i;
            while (true)
            {
                yield return idx.ToList();
                var pos = size - 1;
                while (pos >= 0 && idx[pos] == count - size + pos) pos--;
                if (pos < 0) break;
                idx[pos]++;
                for (var p = pos + 1; p < size; p++) idx[p] = idx[p - 1] + 1;
            }
        }
    }

    private static (double[,], double[], double[,]) Slice(double[,] a, double[] y, double[,] w, bool[] active, int n)
    {
        var dims = Enumerable.Range(0, active.Length).Where(i => active[i]).ToArray();
        var d = dims.Length;
        var aa = new double[d, n];
        var yy = new double[d];
        var ww = new double[d, d];
        for (var i = 0; i < d; i++)
        {
            yy[i] = y[dims[i]];
            for (var k = 0; k < n; k++) aa[i, k] = a[dims[i], k];
            for (var j = 0; j < d; j++) ww[i, j] = w[dims[i], dims[j]];
        }
        return (aa, yy, ww);
    }

    private static double[]? ActiveSetFallback(double[,] ai, double[] yi, double[,] wi, int n, double lo, double hi)
    {
        // 单纯形网格上的受约束最速下降后备（仅在枚举失败时），同样不会截断归一化。
        var best = Unif(n);
        var A = Matrix<double>.Build.DenseOfArray(ai);
        var W = Matrix<double>.Build.DenseOfArray(wi);
        var y = Vector<double>.Build.DenseOfArray(yi);
        var bestObj = ObjectiveP(A, W, y, best);
        for (var t = 0; t < 40; t++)
        {
            var moved = false;
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                if (i == j) continue;
                var step = 0.5 / (1 << t);
                var cand = (double[])best.Clone();
                if (cand[i] - step < lo - 1e-12 || cand[j] + step > hi + 1e-12) continue;
                cand[i] -= step; cand[j] += step;
                var o = ObjectiveP(A, W, y, cand);
                if (o < bestObj - 1e-12) { best = cand; bestObj = o; moved = true; }
            }
            if (!moved) break;
        }
        return best;
    }
}

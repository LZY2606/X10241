namespace IsotopeMix.Core.MathKit;

/// <summary>
/// Exact active-set QP on a bounded simplex:
/// minimize 0.5 xᵀHx + fᵀx  subject to sum(x)=1 and lo[i] ≤ x[i] ≤ hi[i].
/// Bounds come from explicit prior hypotheses only; the simplex constraints
/// themselves are always enforced exactly, never by clip-and-renormalize.
/// </summary>
public static class BoundedSimplexQp
{
    private const double Tol = 1e-9;

    public static QpSolution Solve(double[][] H, double[] f, double[]? lo = null, double[]? hi = null)
    {
        var n = H.Length;
        lo ??= new double[n];
        hi ??= Enumerable.Repeat(1.0, n).ToArray();

        var loSum = lo.Sum();
        if (loSum > 1 + Tol)
            return new QpSolution(QpStatus.Infeasible, new double[n], double.PositiveInfinity,
                Enumerable.Range(0, n).ToArray(), Array.Empty<int>());

        QpSolution? best = null;

        // state: 0 = free interior, 1 = fixed at lo, 2 = fixed at hi
        var totalStates = 1;
        for (var j = 0; j < n; j++) totalStates *= 3;

        for (var stateCode = 0; stateCode < totalStates; stateCode++)
        {
            var states = new int[n];
            var code = stateCode;
            for (var j = 0; j < n; j++) { states[j] = code % 3; code /= 3; }

            var free = new List<int>();
            var atLo = new List<int>();
            var atHi = new List<int>();
            for (var j = 0; j < n; j++)
            {
                if (states[j] == 0) free.Add(j);
                else if (states[j] == 1) atLo.Add(j);
                else atHi.Add(j);
            }

            var fixedSum = atLo.Sum(j => lo![j]) + atHi.Sum(j => hi![j]);
            var freeSum = 1 - fixedSum;
            if (free.Count == 0)
            {
                if (Math.Abs(freeSum) > Tol) continue;
            }

            var x = new double[n];
            foreach (var j in atLo) x[j] = lo![j];
            foreach (var j in atHi) x[j] = hi![j];

            double lambdaSum;
            if (free.Count > 0)
            {
                var k = free.Count;
                var size = k + 1;
                var kkt = LinearAlgebra.Matrix(size, size);
                var rhs = new double[size];
                for (var a = 0; a < k; a++)
                {
                    for (var b = 0; b < k; b++) kkt[a][b] = H[free[a]][free[b]];
                    kkt[a][k] = 1;
                    kkt[k][a] = 1;
                    rhs[a] = -f[free[a]];
                    foreach (var j2 in atLo) rhs[a] -= H[free[a]][j2] * lo![j2];
                    foreach (var j2 in atHi) rhs[a] -= H[free[a]][j2] * hi![j2];
                }
                rhs[k] = freeSum;

                var sol = LinearAlgebra.TrySolve(kkt, rhs);
                if (sol is null) continue;
                lambdaSum = sol[k];

                for (var a = 0; a < k; a++)
                {
                    var xa = sol[a];
                    if (xa < lo![free[a]] - Tol || xa > hi![free[a]] + Tol) goto NextFace;
                    x[free[a]] = Math.Clamp(xa, lo[free[a]], hi[free[a]]);
                }
            }
            else
            {
                lambdaSum = 0;
            }

            var grad = LinearAlgebra.Multiply(H, x);
            for (var j = 0; j < n; j++) grad[j] += f[j];

            // Reduced cost c_j = grad_j + λ. At lower bound c_j ≥ 0; at upper bound c_j ≤ 0.
            foreach (var j in atLo)
                if (grad[j] + lambdaSum < -Tol) goto NextFace;
            foreach (var j in atHi)
                if (grad[j] + lambdaSum > Tol) goto NextFace;
            if (free.Count > 0)
                foreach (var j in free)
                    if (Math.Abs(grad[j] + lambdaSum) > 1e-7) goto NextFace;

            var obj = 0.0;
            for (var i = 0; i < n; i++)
            {
                obj += f[i] * x[i];
                for (var j = 0; j < n; j++) obj += 0.5 * x[i] * H[i][j] * x[j];
            }

            if (best is null || obj < best.Objective - 1e-12)
            {
                best = new QpSolution(QpStatus.Optimal, x, obj,
                    atLo.OrderBy(v => v).ToArray(), free.OrderBy(v => v).ToArray());
            }

            NextFace: ;
        }

        return best ?? new QpSolution(QpStatus.Infeasible, new double[n],
            double.PositiveInfinity, Enumerable.Range(0, n).ToArray(), Array.Empty<int>());
    }
}

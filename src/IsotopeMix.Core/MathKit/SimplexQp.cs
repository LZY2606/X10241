namespace IsotopeMix.Core.MathKit;

public enum QpStatus
{
    Optimal,
    Infeasible
}

public sealed record QpSolution(
    QpStatus Status,
    double[] Proportions,
    double Objective,
    IReadOnlyList<int> ActiveLowerBounds,
    IReadOnlyList<int> FreeVariables);

/// <summary>
/// Exact active-set QP on the probability simplex:
/// minimize 0.5 xᵀHx + fᵀx  subject to sum(x)=1, x ≥ 0.
/// Feasibility is never faked: a failing model is reported as Infeasible rather
/// than clipping negative weights and renormalizing.
/// </summary>
public static class SimplexQp
{
    private const double Tol = 1e-9;

    public static QpSolution Solve(double[][] H, double[] f)
    {
        var n = H.Length;
        if (n == 1)
        {
            return new QpSolution(QpStatus.Optimal, new[] { 1.0 },
                0.5 * H[0][0] + f[0], Array.Empty<int>(), new[] { 0 });
        }

        QpSolution? best = null;

        // Enumerate active-set faces: every variable is either free (x>0) or fixed at 0.
        // For each face solve the KKT stationarity system with sum(x)=1, then verify
        // feasibility of x and non-negativity of the active-constraint multipliers.
        for (var mask = 0; mask < 1 << n; mask++)
        {
            var free = new List<int>();
            var fixedAtZero = new List<int>();
            for (var j = 0; j < n; j++)
            {
                if ((mask & (1 << j)) == 0) free.Add(j);
                else fixedAtZero.Add(j);
            }
            if (free.Count == 0) continue;

            var k = free.Count;
            var Hff = LinearAlgebra.Matrix(k, k);
            var ff = new double[k];
            for (var a = 0; a < k; a++)
            {
                ff[a] = f[free[a]];
                for (var b = 0; b < k; b++) Hff[a][b] = H[free[a]][free[b]];
            }

            // KKT: [ Hff 1 ] [x]   [-ff]
            //      [ 1ᵀ   0 ] [λ] = [ 1 ]
            var size = k + 1;
            var kkt = LinearAlgebra.Matrix(size, size);
            var rhs = new double[size];
            for (var a = 0; a < k; a++)
            {
                for (var b = 0; b < k; b++) kkt[a][b] = Hff[a][b];
                kkt[a][k] = 1;
                kkt[k][a] = 1;
                rhs[a] = -ff[a];
            }
            rhs[k] = 1;

            var sol = LinearAlgebra.TrySolve(kkt, rhs);
            if (sol is null) continue;

            var x = new double[n];
            for (var a = 0; a < k; a++)
            {
                var xa = sol[a];
                if (xa < -Tol) goto NextFace;
                x[free[a]] = xa < 0 ? 0 : xa;
            }
            foreach (var j in fixedAtZero) x[j] = 0;

            // KKT multiplier for each fixed-at-zero variable:
            // stationarity (Hx + f)_j + lambda_sum - nu_j = 0 with x_j=0, so
            // nu_j = (Hx + f)_j + lambda_sum; require nu_j >= 0.
            var lambdaSum = sol[k];
            var grad = LinearAlgebra.Multiply(H, x);
            for (var j = 0; j < n; j++) grad[j] += f[j];
            foreach (var j in fixedAtZero)
            {
                var nu = grad[j] + lambdaSum;
                if (nu < -Tol) goto NextFace;
            }

            var obj = 0.0;
            for (var i = 0; i < n; i++)
            {
                obj += f[i] * x[i];
                for (var j = 0; j < n; j++) obj += 0.5 * x[i] * H[i][j] * x[j];
            }

            if (best is null || obj < best.Objective - 1e-12)
            {
                best = new QpSolution(QpStatus.Optimal, x, obj,
                    fixedAtZero.OrderBy(v => v).ToArray(), free.OrderBy(v => v).ToArray());
            }

            NextFace: ;
        }

        return best ?? new QpSolution(QpStatus.Infeasible, new double[n],
            double.PositiveInfinity, Enumerable.Range(0, n).ToArray(), Array.Empty<int>());
    }
}

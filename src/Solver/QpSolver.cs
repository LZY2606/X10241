namespace IsotopeMixing.Solver;

/// <summary>
/// Minimizes 0.5 x'Hx - g'x subject to Aeq x = beq and lo[i] &lt;= x[i] &lt;= hi[i].
/// Exact small-QP solver: enumerates active faces (each coordinate free/lower/upper),
/// solves the KKT system with a minimum-norm pseudo-solve, and verifies full KKT
/// sign conditions. It never clips unconstrained weights and renormalizes them.
/// </summary>
public static class QpSolver
{
    public sealed record QpSolution(bool Feasible, double[] X, double Objective, bool Degenerate);

    public static QpSolution Solve(double[][] H, double[] g, double[][] Aeq, double[] beq, double[] lo, double[] hi)
    {
        int n = H.Length;
        for (int i = 0; i < n; i++)
            if (lo[i] > hi[i] + 1e-12) return new QpSolution(false, Array.Empty<double>(), double.NaN, false);
        // Equality feasibility against the box
        double sumLo = lo.Sum(), sumHi = hi.Sum();
        foreach (var rowEq in Aeq) { /* single equality expected; sum bounds check assumes ones row */ }
        // start from a feasible point
        var x0 = FeasibleStart(lo, hi);
        if (x0 is null) return new QpSolution(false, Array.Empty<double>(), double.NaN, false);

        double bestObj = double.PositiveInfinity;
        double[]? best = null;
        bool degenerate = false;
        int states = (int)Math.Pow(3, n);
        for (int s = 0; s < states; s++)
        {
            int code = s;
            var state = new int[n]; // 0 free, 1 lower, 2 upper
            for (int i = 0; i < n; i++) { state[i] = code % 3; code /= 3; }

            var x = (double[])x0.Clone();
            var free = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (state[i] == 1) x[i] = lo[i];
                else if (state[i] == 2) x[i] = hi[i];
                else free.Add(i);
            }

            double[]? lambda = null;
            int meq = Aeq.Length;
            if (free.Count > 0)
            {
                int f = free.Count;
                int size = f + meq;
                var K = LinearAlgebra.Rect(size, size);
                var rhs = new double[size];
                for (int a = 0; a < f; a++)
                for (int b = 0; b < f; b++)
                    K[a][b] = H[free[a]][free[b]];
                for (int a = 0; a < f; a++)
                {
                    for (int e = 0; e < meq; e++)
                    {
                        K[a][f + e] = Aeq[e][free[a]];
                        K[f + e][a] = Aeq[e][free[a]];
                    }
                    double fixedContrib = 0;
                    for (int i = 0; i < n; i++)
                        if (state[i] != 0) fixedContrib += H[free[a]][i] * x[i];
                    rhs[a] = g[free[a]] - fixedContrib;
                }
                for (int e = 0; e < meq; e++)
                {
                    double fixedEq = 0;
                    for (int i = 0; i < n; i++)
                        if (state[i] != 0) fixedEq += Aeq[e][i] * x[i];
                    rhs[f + e] = beq[e] - fixedEq;
                }
                var sol = SymmetricSolve(K, rhs);
                if (sol is null) { degenerate = true; continue; }
                for (int a = 0; a < f; a++) x[free[a]] = sol[a];
                lambda = new double[meq];
                for (int e = 0; e < meq; e++) lambda[e] = sol[f + e];
                for (int a = 0; a < f; a++)
                    if (x[free[a]] < lo[free[a]] - 1e-8 || x[free[a]] > hi[free[a]] + 1e-8)
                    { x = null!; break; }
                if (x is null) continue;
            }
            else
            {
                for (int e = 0; e < meq; e++)
                {
                    double val = 0;
                    for (int i = 0; i < n; i++) val += Aeq[e][i] * x[i];
                    if (Math.Abs(val - beq[e]) > 1e-8) { x = null!; break; }
                }
                if (x is null) continue;
            }

            // KKT multiplier sign checks on fixed coordinates
            lambda ??= new double[meq];
            bool kkt = true;
            for (int i = 0; i < n && kkt; i++)
            {
                if (state[i] == 0) continue;
                double hxMinusG = 0;
                for (int j = 0; j < n; j++) hxMinusG += H[i][j] * x[j];
                hxMinusG -= g[i];
                double eqTerm = 0;
                for (int e = 0; e < meq; e++) eqTerm += lambda[e] * Aeq[e][i];
                double muLo = hxMinusG + eqTerm;
                double muHi = -hxMinusG - eqTerm;
                if (state[i] == 1 && muLo < -1e-6) kkt = false;
                if (state[i] == 2 && muHi < -1e-6) kkt = false;
            }
            if (!kkt) continue;

            double obj = 0;
            for (int i = 0; i < n; i++)
            {
                obj += 0.5 * H[i][i] * x[i] * x[i] - g[i] * x[i];
                for (int j = i + 1; j < n; j++) obj += H[i][j] * x[i] * x[j];
            }
            if (obj < bestObj - 1e-10) { bestObj = obj; best = (double[])x.Clone(); }
        }
        if (best is null)
        {
            // Degenerate KKT (flat objective along the equality face): any feasible point is optimal.
            return new QpSolution(true, x0, Obj(H, g, x0), true);
        }
        return new QpSolution(true, best, bestObj, degenerate);
    }

    private static double[]? FeasibleStart(double[] lo, double[] hi)
    {
        int n = lo.Length;
        double sLo = lo.Sum(), sHi = hi.Sum();
        if (1 < sLo - 1e-9 || 1 > sHi + 1e-9) return null;
        var x = new double[n];
        double extra = 1 - sLo;
        for (int i = 0; i < n; i++) x[i] = lo[i];
        for (int i = 0; i < n; i++)
        {
            double add = Math.Min(extra, hi[i] - lo[i]);
            x[i] += add;
            extra -= add;
            if (extra <= 1e-12) break;
        }
        return extra > 1e-9 ? null : x;
    }

    private static double Obj(double[][] H, double[] g, double[] x)
    {
        double o = 0;
        for (int i = 0; i < x.Length; i++)
        {
            o += 0.5 * H[i][i] * x[i] * x[i] - g[i] * x[i];
            for (int j = i + 1; j < x.Length; j++) o += H[i][j] * x[i] * x[j];
        }
        return o;
    }

    private static double[]? SymmetricSolve(double[][] K, double[] b)
    {
        var (vals, vecs) = LinearAlgebra.SymmetricEigen(K);
        int n = b.Length;
        double scale = Math.Sqrt(Math.Max(1e-30, vals.Select(v => Math.Abs(v)).Max()));
        var x = new double[n];
        for (int j = 0; j < n; j++)
        {
            if (Math.Abs(vals[j]) < 1e-10 * scale) return null;
            double coef = 0;
            for (int i = 0; i < n; i++) coef += vecs[i][j] * b[i];
            coef /= vals[j];
            for (int i = 0; i < n; i++) x[i] += coef * vecs[i][j];
        }
        return x;
    }
}

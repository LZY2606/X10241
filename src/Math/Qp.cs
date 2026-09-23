using System;
using System.Collections.Generic;
using System.Linq;

namespace IsoMix.MathUtils;

public sealed record QpSolution(
    bool Feasible,
    double[] X,
    double Objective,
    bool InfeasibleFromBounds,
    int Iterations);

/// <summary>
/// 主动集法求解:
///   min  0.5 x'Hx + g'x
///   s.t. e'x = 1,  lower[i] &lt;= x[i] &lt;= upper[i],  x&gt;=0
/// 约束在求解器内部严格成立；调用方不需要任何"截断后归一化"。
/// </summary>
public static class Qp
{
    private const double Tol = 1e-10;

    public static QpSolution Solve(double[][] h, double[] g, double[] lower, double[] upper)
    {
        int n = h.Length;
        for (int i = 0; i < n; i++) { lower[i] = Math.Max(0, lower[i]); upper[i] = Math.Min(1, upper[i]); }

        double sumL = lower.Sum(), sumU = upper.Sum();
        if (sumL > 1 + 1e-8 || sumU < 1 - 1e-8)
            return new QpSolution(false, new double[n], double.NaN, true, 0);

        var x = FindInitialFeasible(lower, upper);
        var at = new Bound[n];
        for (int i = 0; i < n; i++)
            at[i] = Math.Abs(x[i] - lower[i]) < 1e-10 ? Bound.Lower
                  : Math.Abs(x[i] - upper[i]) < 1e-10 ? Bound.Upper : Bound.Free;

        int iter = 0, maxIter = 200 + 40 * n;
        while (iter++ < maxIter)
        {
            var free = Enumerable.Range(0, n).Where(i => at[i] == Bound.Free).ToArray();
            if (free.Length == 0)
            {
                var (rl, ru) = Multipliers(h, g, x, at, free);
                int? release = PickRelease(at, rl, ru);
                if (release is null) break;
                at[release.Value] = Bound.Free;
                continue;
            }

            double[] xNew = SolveEqualitySubproblem(h, g, x, at, free);
            if (xNew is null)
            {
                int? release = MultipliersAndPick(h, g, x, at, free);
                if (release is null) break;
                at[release.Value] = Bound.Free;
                continue;
            }

            double step = 1.0;
            int blocker = -1;
            Bound blockerKind = Bound.Free;
            for (int idx = 0; idx < free.Length; idx++)
            {
                int i = free[idx];
                double p = xNew[idx] - x[i];
                if (p > 1e-12 && upper[i] - x[i] < upper[i] - lower[i] - 1e-12)
                {
                    double a = (upper[i] - x[i]) / p;
                    if (a < step) { step = a; blocker = i; blockerKind = Bound.Upper; }
                }
                else if (p < -1e-12)
                {
                    double a = (lower[i] - x[i]) / p;
                    if (a < step) { step = a; blocker = i; blockerKind = Bound.Lower; }
                }
            }

            double stepNorm = 0;
            for (int idx = 0; idx < free.Length; idx++)
            {
                int i = free[idx];
                double p = xNew[idx] - x[i];
                stepNorm += p * p;
                x[i] += step * p;
                if (Math.Abs(x[i]) < 1e-12) x[i] = 0;
            }
            // 等式恒成立（子问题保证），浮点修补只在最后做一次且总和精确归一
            double total = x.Sum();
            if (Math.Abs(total - 1) > 1e-12)
            {
                int any = Enumerable.Range(0, n).First(i => at[i] == Bound.Free);
                x[any] += 1 - total;
            }

            if (stepNorm < 1e-20 || blocker < 0)
            {
                var (rl, ru) = Multipliers(h, g, x, at, free);
                int? release = PickRelease(at, rl, ru);
                if (release is null) break;
                at[release.Value] = Bound.Free;
            }
            else
            {
                at[blocker] = blockerKind;
            }
        }

        double obj = 0.5 * Quadratic(h, x) + Dot(g, x);
        return new QpSolution(true, x, obj, false, iter);
    }

    private static double[] FindInitialFeasible(double[] lower, double[] upper)
    {
        int n = lower.Length;
        var x = (double[])lower.Clone();
        double rem = 1 - x.Sum();
        for (int i = 0; i < n && rem > 1e-12; i++)
        {
            double add = Math.Min(rem, upper[i] - x[i]);
            x[i] += add;
            rem -= add;
        }
        return x;
    }

    private enum Bound { Free, Lower, Upper }

    private static double[]? SolveEqualitySubproblem(double[][] h, double[] g, double[] x, Bound[] at, int[] free)
    {
        int m = free.Length;
        int size = m + 1;
        var kkt = new double[size][];
        for (int r = 0; r < size; r++) kkt[r] = new double[size];
        var rhs = new double[size];
        var fixedSum = Enumerable.Range(0, x.Length).Where(i => at[i] != Bound.Free).Sum(i => x[i]);

        for (int a = 0; a < m; a++)
        {
            int i = free[a];
            rhs[a] = -g[i];
            for (int b = 0; b < m; b++)
            {
                int j = free[b];
                kkt[a][b] = h[i][j];
                rhs[a] -= (at[j] == Bound.Free ? 0 : h[i][j] * x[j]);
            }
            kkt[a][m] = 1;
        }
        for (int a = 0; a < m; a++) kkt[m][a] = 1;
        rhs[m] = 1 - fixedSum;

        var sol = GaussianSolve(kkt, rhs);
        if (sol is null) return null;
        var xNew = (double[])x.Clone();
        for (int a = 0; a < m; a++) xNew[free[a]] = sol[a];
        return xNew;
    }

    private static double[]? GaussianSolve(double[][] a, double[] b)
    {
        int n = b.Length;
        var m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[n + 1];
            Array.Copy(a[i], m[i], n);
            m[i][n] = b[i];
        }
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r][col]) > Math.Abs(m[pivot][col])) pivot = r;
            if (Math.Abs(m[pivot][col]) < 1e-14) return null;
            (m[pivot], m[col]) = (m[col], m[pivot]);
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = m[r][col] / m[col][col];
                for (int c = col; c <= n; c++) m[r][c] -= f * m[col][c];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = m[i][n] / m[i][i];
        return x;
    }

    private static (double[] rLower, double[] rUpper) Multipliers(double[][] h, double[] g, double[] x, Bound[] at, int[] free)
    {
        int n = x.Length;
        int m = free.Length;
        double lambda = 0;
        if (m > 0)
        {
            // lambda from stationarity of any free row: r=0 => lambda = -(Hx+g)_i
            int i = free[0];
            lambda = -(RowHx(h, x, i) + g[i]);
        }
        var rl = new double[n];
        var ru = new double[n];
        for (int i = 0; i < n; i++)
        {
            double r = RowHx(h, x, i) + g[i] + lambda;
            if (at[i] == Bound.Lower) rl[i] = -r; // 下界 μ_L = -r，需 &gt;=0
            if (at[i] == Bound.Upper) ru[i] = r;  // 上界 μ_U =  r，需 &gt;=0
        }
        return (rl, ru);
    }

    private static int? MultipliersAndPick(double[][] h, double[] g, double[] x, Bound[] at, int[] free)
    {
        var (rl, ru) = Multipliers(h, g, x, at, free);
        return PickRelease(at, rl, ru);
    }

    private static int? PickRelease(Bound[] at, double[] rl, double[] ru)
    {
        int? best = null;
        double worst = -1e-9;
        for (int i = 0; i < at.Length; i++)
        {
            if (at[i] == Bound.Lower && rl[i] < worst) { worst = rl[i]; best = i; }
            else if (at[i] == Bound.Upper && ru[i] < worst) { worst = ru[i]; best = i; }
        }
        return best;
    }

    private static double RowHx(double[][] h, double[] x, int i)
    {
        double s = 0;
        for (int j = 0; j < x.Length; j++) s += h[i][j] * x[j];
        return s;
    }

    private static double Quadratic(double[][] h, double[] x)
    {
        double s = 0;
        for (int i = 0; i < x.Length; i++)
            for (int j = 0; j < x.Length; j++)
                s += x[i] * h[i][j] * x[j];
        return s;
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}

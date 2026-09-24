using System;
using System.Collections.Generic;
using System.Linq;

namespace IsotopeMixing.Core.Engine;

/// <summary>简化坐标 y ∈ R^d (d=m-1) 上的多面体 A y <= b。</summary>
public sealed class Polytope
{
    public required int Dim { get; init; }
    public required List<double[]> A { get; init; }
    public required List<double> B { get; init; }
    public required List<double[]> Vertices { get; init; }

    private const double Tol = 1e-9;

    public bool FeasiblePoint(double[] y)
    {
        for (int i = 0; i < A.Count; i++)
            if (Inner(A[i], y) > B[i] + 1e-7) return false;
        return true;
    }

    /// <summary>单纯形（比例非负+和为一）的全部顶点。</summary>
    public static double[][] SimplexVertices(int m)
    {
        int d = m - 1;
        var verts = new List<double[]> { new double[d] };
        for (int k = 0; k < d; k++)
        {
            var y = new double[d];
            y[k] = 1;
            verts.Add(y);
        }
        return verts.ToArray();
    }

    public static Polytope Build(int m, IReadOnlyList<(int emIndex, double min, double max)>? bounds)
    {
        int d = m - 1;
        var rows = new List<double[]>();
        var rhs = new List<double>();
        var zeros = new double[d];
        // -y_k <= 0  与  sum y <= 1
        for (int k = 0; k < d; k++)
        {
            var neg = new double[d]; neg[k] = -1; rows.Add(neg); rhs.Add(0.0);
        }
        var ones = Enumerable.Repeat(1.0, d).ToArray();
        rows.Add(ones); rhs.Add(1.0);
        if (bounds is not null)
        {
            foreach (var (emIndex, min, max) in bounds)
            {
                if (min > 0)
                {
                    if (emIndex < d) { var row = new double[d]; row[emIndex] = -1; rows.Add(row); rhs.Add(-min); }
                    else { rows.Add((double[])ones.Clone()); rhs.Add(1 - min); }
                }
                if (max < 1)
                {
                    if (emIndex < d) { var row = new double[d]; row[emIndex] = 1; rows.Add(row); rhs.Add(max); }
                    else { var row = new double[d]; for (int k = 0; k < d; k++) row[k] = -1; rows.Add(row); rhs.Add(- (1 - max)); }
                }
            }
        }
        var verts = EnumerateVertices(d, rows, rhs);
        return new Polytope { Dim = d, A = rows, B = rhs, Vertices = verts };
    }

    private static List<double[]> EnumerateVertices(int d, List<double[]> rows, List<double> rhs)
    {
        var simplex = d == 0 ? new List<double[]> { Array.Empty<double>() } : SimplexVertices(d + 1).ToList();
        var points = new List<double[]>(simplex);
        // 边界约束两两交点（2D 精确；高维也收集候选再验证）
        for (int i = 0; i < rows.Count; i++)
            for (int j = i + 1; j < rows.Count; j++)
            {
                if (d == 1)
                {
                    if (Math.Abs(rows[i][0]) > Tol)
                    {
                        var y = new[] { rhs[i] / rows[i][0] };
                        points.Add(y);
                    }
                    continue;
                }
                if (d == 2)
                {
                    double det = rows[i][0] * rows[j][1] - rows[i][1] * rows[j][0];
                    if (Math.Abs(det) < 1e-10) continue;
                    double y0 = (rhs[i] * rows[j][1] - rows[i][1] * rhs[j]) / det;
                    double y1 = (rows[i][0] * rhs[j] - rhs[i] * rows[j][0]) / det;
                    points.Add(new[] { y0, y1 });
                }
            }
        var feasible = points.Where(p => Satisfies(p, rows, rhs)).ToList();
        // 去重
        var unique = new List<double[]>();
        foreach (var p in feasible)
            if (!unique.Any(q => Dist(p, q) < 1e-7)) unique.Add(p);
        return unique;
    }

    public static bool Satisfies(double[] y, List<double[]> rows, List<double> rhs)
    {
        for (int i = 0; i < rows.Count; i++)
            if (Inner(rows[i], y) > rhs[i] + 1e-7) return false;
        return true;
    }

    /// <summary>在多面体上最小化 0.5 (y-y0)^T H (y-y0) - g^T(y-y0)（此处用通用凸二次 0.5 yHy + c y）。</summary>
    public double[] MinimizeQuadratic(double[,] h, double[] c)
    {
        if (Dim == 0) return Array.Empty<double>();
        // 从最好顶点出发做投影梯度 + 边界线搜索
        double[] best = Vertices.Count > 0
            ? Vertices.OrderBy(v => QuadVal(v, h, c)).First()
            : new double[Dim];
        double bestVal = QuadVal(best, h, c);
        double[] y = (double[])best.Clone();
        for (int iter = 0; iter < 200; iter++)
        {
            var grad = new double[Dim];
            for (int i = 0; i < Dim; i++)
            {
                double s = c[i];
                for (int j = 0; j < Dim; j++) s += h[i, j] * y[j];
                grad[i] = s;
            }
            // 投影梯度步（简单回溯），方向 -grad
            double step = 1.0;
            double[]? candidate = null;
            for (int ls = 0; ls < 40; ls++)
            {
                var trial = y.Zip(grad, (yi, gi) => yi - step * gi).ToArray();
                var proj = Project(trial);
                if (QuadVal(proj, h, c) < bestVal - 1e-12) { candidate = proj; break; }
                step *= 0.5;
            }
            if (candidate is null) break;
            double cv = QuadVal(candidate, h, c);
            if (bestVal - cv < 1e-11) { best = candidate; break; }
            y = candidate; bestVal = cv; best = candidate;
        }
        return best;
    }

    private double QuadVal(double[] y, double[,] h, double[] c)
    {
        double s = 0;
        for (int i = 0; i < Dim; i++)
        {
            s += c[i] * y[i];
            for (int j = 0; j < Dim; j++) s += 0.5 * h[i, j] * y[i] * y[j];
        }
        return s;
    }

    /// <summary>投影到多面体：迭代投影到激活半空间（维数低，枚举顶点+最近邻兜底）。</summary>
    public double[] Project(double[] y0)
    {
        if (FeasiblePoint(y0)) return y0;
        // d<=2：约束交点与顶点中取投影最近点，再做交点到 y0 线段裁剪
        if (Dim <= 2)
        {
            var candidates = new List<double[]>(Vertices);
            for (int i = 0; i < A.Count; i++)
            {
                var p = ProjectToBoundary(y0, i);
                if (p is not null && Satisfies(p, A, B)) candidates.Add(p);
            }
            return candidates.OrderBy(p => Dist2(p, y0)).First();
        }
        // 高维：简化为最近顶点 + 沿到各顶点方向裁剪
        var cands = new List<double[]>(Vertices);
        foreach (var v in Vertices)
        {
            var p = ClipFromOutside(y0, v);
            if (p is not null) cands.Add(p);
        }
        return cands.OrderBy(p => Dist2(p, y0)).First();
    }

    private double[]? ProjectToBoundary(double[] y0, int row)
    {
        var nrm = A[row];
        double n2 = Inner(nrm, nrm);
        if (n2 < 1e-14) return null;
        double t = (Inner(nrm, y0) - B[row]) / n2;
        return y0.Zip(nrm, (yi, ni) => yi - t * ni).ToArray();
    }

    /// <summary>从多面体外点 y0 向内部点 inside 移动，返回边界点。</summary>
    public double[]? ClipFromOutside(double[] y0, double[] inside)
    {
        if (FeasiblePoint(y0)) return y0;
        double hi = 1;
        for (int i = 0; i < A.Count; i++)
        {
            double denom = Inner(A[i], inside) - Inner(A[i], y0);
            if (denom <= 1e-14) continue;
            double t = (B[i] - Inner(A[i], y0)) / denom;
            if (t >= 0 && t <= 1) hi = Math.Min(hi, t);
        }
        if (hi < 0 || hi > 1 + 1e-9) return null;
        return y0.Zip(inside, (a, b) => a + hi * (b - a)).ToArray();
    }

    public static double Inner(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
    private static double Dist(double[] a, double[] b) => Math.Sqrt(Dist2(a, b));
    public static double Dist2(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]) * (a[i] - b[i]);
        return s;
    }
}

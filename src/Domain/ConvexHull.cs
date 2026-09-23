namespace IsotopeMix.Domain;

/// <summary>低维凸包：2D/3D 点包含判定与 2D Andrew 单调链（SVG 画可行区）。</summary>
public static class ConvexHull
{
    public static bool Contains(IReadOnlyList<double[]> hullPoints, double[] point, int dimensions)
    {
        if (dimensions == 1)
        {
            double lo = hullPoints.Min(p => p[0]) - 1e-9;
            double hi = hullPoints.Max(p => p[0]) + 1e-9;
            return point[0] >= lo && point[0] <= hi;
        }
        if (dimensions == 2)
        {
            var hull = In2D(hullPoints.Select(p => new[] { p[0], p[1] }).ToList());
            return Contains2D(hull, point[0], point[1]);
        }
        return ContainsSimplexGeneral(hullPoints, point);
    }

    public static List<double[]> In2D(List<double[]> points)
    {
        var ordered = points.OrderBy(p => (p[0], p[1])).ToList();
        if (ordered.Count <= 1) return ordered;
        var lower = new List<double[]>();
        foreach (var p in ordered)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<double[]>();
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            var p = ordered[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return lower.Concat(upper).ToList();
    }

    private static double Cross(double[] o, double[] a, double[] b)
        => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

    private static bool Contains2D(IReadOnlyList<double[]> hull, double x, double y)
    {
        if (hull.Count == 1) return Math.Abs(hull[0][0] - x) < 1e-9 && Math.Abs(hull[0][1] - y) < 1e-9;
        bool hasNeg = false, hasPos = false;
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            double cross = (b[0] - a[0]) * (y - a[1]) - (b[1] - a[1]) * (x - a[0]);
            if (cross > 1e-8) hasPos = true;
            if (cross < -1e-8) hasNeg = true;
            if (hasPos && hasNeg) return false;
        }
        return true;
    }

    /// <summary>
    /// 一般维度：直接解 A f = point, Σf=1 的最小二乘并检查 f≥0；
    /// 用于"样品是否超出端元包络"的保守判定。
    /// </summary>
    private static bool ContainsSimplexGeneral(IReadOnlyList<double[]> pts, double[] point)
    {
        int d = point.Length, m = pts.Count;
        var design = new double[d + 1, m];
        var rhs = new double[d + 1];
        for (int k = 0; k < m; k++)
        {
            for (int i = 0; i < d; i++) design[i, k] = pts[k][i];
            design[d, k] = 1;
        }
        rhs[d] = 1;
        for (int i = 0; i < d; i++) rhs[i] = point[i];
        var ata = new double[m, m];
        var atb = new double[m];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int r = 0; r <= d; r++) s += design[r, i] * design[r, j];
                ata[i, j] = s;
            }
        for (int i = 0; i < m; i++)
        {
            double s = 0;
            for (int r = 0; r <= d; r++) s += design[r, i] * rhs[r];
            atb[i] = s;
        }
        var f = LinearAlgebra.QuadraticSolve(ata, atb);
        double sum = f.Sum();
        return f.All(v => v >= -1e-7) && Math.Abs(sum - 1) < 1e-6;
    }
}

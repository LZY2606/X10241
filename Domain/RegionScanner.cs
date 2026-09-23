using MathNet.Numerics.LinearAlgebra;

namespace IsotopeMix.Domain;

/// <summary>
/// 在单纯形上扫描 χ² 阈值内的可行比例区域（不靠单点估计伪造）。
/// - n=2：给出一维可行区间（可天然多段、不连通）
/// - n=3：栅格标记可行点，连通分量各自取边界（多个可行区可被显式保留）
/// - n=4：三维单纯形上采样，保留全部可行点供高维投影显示
/// </summary>
public static class RegionScanner
{
    public sealed record ScanResult(FeasibleRegion Region, double[]? BestGrid);

    public static ScanResult Scan(double[,] a, double[] y, double[,] w, bool[] activeDims,
        int n, double threshold2, double lo, double hi)
    {
        if (n == 2) return Scan1D(a, y, w, activeDims, n, threshold2, lo, hi);
        if (n == 3) return Scan2D(a, y, w, activeDims, n, threshold2, lo, hi);
        return ScanHighD(a, y, w, activeDims, n, threshold2, lo, hi);
    }

    private static ScanResult Scan1D(double[,] a, double[] y, double[,] w, bool[] active,
        int n, double threshold2, double lo, double hi)
    {
        var dims = DimList(active);
        var res = 400;
        bool[] feas = new bool[res + 1];
        var bestT = 0.5; var bestD = double.PositiveInfinity;
        for (var i = 0; i <= res; i++)
        {
            var t = (double)i / res;
            var p = new[] { t, 1 - t };
            if (!BoundsOk(p, lo, hi)) continue;
            var d2 = Mahal(a, y, w, dims, p);
            feas[i] = d2 <= threshold2;
            if (d2 < bestD) { bestD = d2; bestT = t; }
        }
        var intervals = new List<double>();
        for (var i = 0; i <= res; i++)
        {
            if (feas[i] && (i == 0 || !feas[i - 1])) intervals.Add((double)i / res);
            if (feas[i] && (i == res || !feas[i + 1])) intervals.Add((double)i / res);
        }
        var best = new[] { bestT, 1 - bestT };
        if (intervals.Count == 0)
            return new ScanResult(new FeasibleRegion(RegionKind.Empty, Array.Empty<double>(),
                Array.Empty<double[]>(), false, Array.Empty<CandidatePolygon>()), best);
        var verts = intervals.SelectMany((v, idx) =>
        {
            var p0 = new[] { v, 1 - v };
            return new[] { p0 };
        }).ToList();
        var disjoint = intervals.Count > 2;
        var comps = new List<CandidatePolygon>();
        for (var k = 0; k < intervals.Count; k += 2)
            comps.Add(new CandidatePolygon(new[] { new[] { intervals[k], 1 - intervals[k] }, new[] { intervals[k + 1], 1 - intervals[k + 1] } }, RegionKind.Intervals));
        return new ScanResult(new FeasibleRegion(RegionKind.Intervals, intervals, verts, disjoint, comps), best);
    }

    private static ScanResult Scan2D(double[,] a, double[] y, double[,] w, bool[] active,
        int n, double threshold2, double lo, double hi)
    {
        var dims = DimList(active);
        var res = 96;
        var grid = new bool[res + 1, res + 1];
        var bestP = new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 };
        var bestD = double.PositiveInfinity;
        var count = 0;
        for (var i = 0; i <= res; i++)
        for (var j = 0; j <= res - i; j++)
        {
            var p1 = (double)i / res;
            var p2 = (double)j / res;
            var p = new[] { p1, p2, 1 - p1 - p2 };
            if (!BoundsOk(p, lo, hi)) { grid[i, j] = false; continue; }
            var d2 = Mahal(a, y, w, dims, p);
            grid[i, j] = d2 <= threshold2;
            if (grid[i, j]) count++;
            if (d2 < bestD) { bestD = d2; bestP = p; }
        }
        if (count == 0)
            return new ScanResult(new FeasibleRegion(RegionKind.Empty, Array.Empty<double>(),
                Array.Empty<double[]>(), false, Array.Empty<CandidatePolygon>()), bestP);

        // 连通分量（栅格 4 邻域）
        var label = new int[res + 1, res + 1];
        var components = new List<List<(int I, int J)>>();
        for (var i = 0; i <= res; i++)
        for (var j = 0; j <= res - i; j++)
        {
            if (!grid[i, j] || label[i, j] != 0) continue;
            var id = components.Count + 1;
            var comp = new List<(int, int)>();
            var stack = new Stack<(int, int)>();
            stack.Push((i, j));
            while (stack.Count > 0)
            {
                var (ci, cj) = stack.Pop();
                if (ci < 0 || cj < 0 || ci + cj > res || !grid[ci, cj] || label[ci, cj] != 0) continue;
                label[ci, cj] = id;
                comp.Add((ci, cj));
                stack.Push((ci + 1, cj)); stack.Push((ci - 1, cj));
                stack.Push((ci, cj + 1)); stack.Push((ci, cj - 1));
            }
            components.Add(comp);
        }

        var allVerts = new List<double[]>();
        var polys = new List<CandidatePolygon>();
        foreach (var comp in components)
        {
            // 凸包近似该连通分量（可行域本身为凸集；多分量情形分别保留）
            var pts = comp.Select(c => new double[] { (double)c.I / res, (double)c.J / res })
                .Select(q => new double[] { q[0], q[1], 1 - q[0] - q[1] }).ToList();
            var hull = ConvexHull2D(pts);
            polys.Add(new CandidatePolygon(hull, RegionKind.Polygon));
            allVerts.AddRange(hull);
        }
        return new ScanResult(new FeasibleRegion(RegionKind.Polygon, Array.Empty<double>(),
            allVerts, components.Count > 1, polys), bestP);
    }

    private static ScanResult ScanHighD(double[,] a, double[] y, double[,] w, bool[] active,
        int n, double threshold2, double lo, double hi)
    {
        var dims = DimList(active);
        var perEdge = n == 4 ? 34 : 16;
        var feasible = new List<double[]>();
        var bestP = Enumerable.Repeat(1.0 / n, n).ToArray();
        var bestD = double.PositiveInfinity;
        foreach (var p in SimplexGrid(n, perEdge))
        {
            if (!BoundsOk(p, lo, hi)) continue;
            var d2 = Mahal(a, y, w, dims, p);
            if (d2 < bestD) { bestD = d2; bestP = p; }
            if (d2 <= threshold2) feasible.Add(p);
        }
        if (feasible.Count == 0)
            return new ScanResult(new FeasibleRegion(RegionKind.Empty, Array.Empty<double>(),
                Array.Empty<double[]>(), false, Array.Empty<CandidatePolygon>()), bestP);
        // 高维多连通分量在 3D 单纯形内判定复杂；以投影凸包呈现，不宣称 disjoint。
        return new ScanResult(new FeasibleRegion(RegionKind.Polytope, Array.Empty<double>(),
            feasible, false, new[] { new CandidatePolygon(feasible, RegionKind.Polytope) }), bestP);
    }

    public static IEnumerable<double[]> SimplexGrid(int n, int perEdge)
    {
        if (n == 1) { yield return new[] { 1.0 }; yield break; }
        if (n == 2)
        {
            for (var i = 0; i <= perEdge; i++) { var t = (double)i / perEdge; yield return new[] { t, 1 - t }; }
            yield break;
        }
        if (n == 3)
        {
            for (var i = 0; i <= perEdge; i++)
            for (var j = 0; j <= perEdge - i; j++)
            {
                var p1 = (double)i / perEdge; var p2 = (double)j / perEdge;
                yield return new[] { p1, p2, 1 - p1 - p2 };
            }
            yield break;
        }
        // n == 4
        for (var i = 0; i <= perEdge; i++)
        for (var j = 0; j <= perEdge - i; j++)
        for (var k = 0; k <= perEdge - i - j; k++)
        {
            var p1 = (double)i / perEdge; var p2 = (double)j / perEdge; var p3 = (double)k / perEdge;
            yield return new[] { p1, p2, p3, 1 - p1 - p2 - p3 };
        }
    }

    private static bool BoundsOk(double[] p, double lo, double hi) =>
        p.All(v => v >= lo - 1e-9 && v <= hi + 1e-9) && Math.Abs(p.Sum() - 1) < 1e-9;

    private static int[] DimList(bool[] active) =>
        Enumerable.Range(0, active.Length).Where(i => active[i]).ToArray();

    private static double Mahal(double[,] a, double[] y, double[,] w, int[] dims, double[] p)
    {
        var d = dims.Length;
        var r = new double[d];
        for (var i = 0; i < d; i++)
        {
            double pred = 0;
            for (var k = 0; k < p.Length; k++) pred += a[dims[i], k] * p[k];
            r[i] = pred - y[dims[i]];
        }
        var rv = Vector<double>.Build.DenseOfArray(r);
        var ww = Matrix<double>.Build.Dense(d, d);
        for (var i = 0; i < d; i++)
        for (var j = 0; j < d; j++) ww[i, j] = w[dims[i], dims[j]];
        return rv.DotProduct(ww * rv);
    }

    /// <summary>单调链凸包（输入点在 p1-p2 平面；按 p1,p2 排序）。</summary>
    private static List<double[]> ConvexHull2D(List<double[]> pts)
    {
        var sorted = pts.OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
        if (sorted.Count <= 3) return sorted;
        double Cross(double[] o, double[] a, double[] b) =>
            (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
        var lower = new List<double[]>();
        foreach (var pt in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], pt) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(pt);
        }
        var upper = new List<double[]>();
        for (var idx = sorted.Count - 1; idx >= 0; idx--)
        {
            var pt = sorted[idx];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], pt) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(pt);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }
}

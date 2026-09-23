using System;
using System.Collections.Generic;

namespace IsoMix.MathUtils;

/// <summary>规则单纯形网格枚举：所有点严格满足 f&gt;=0 且 sum f=1（不靠事后截断）。</summary>
public static class Simplex
{
    /// <param name="sources">端元数 n</param>
    /// <param name="grid">每条边分段数</param>
    public static IEnumerable<double[]> Grid(int sources, int grid)
    {
        foreach (var counts in Recurse(sources, grid))
        {
            var f = new double[sources];
            for (int i = 0; i < sources; i++) f[i] = (double)counts[i] / grid;
            yield return f;
        }
    }

    private static IEnumerable<int[]> Recurse(int dims, int budget)
    {
        if (dims == 1)
        {
            yield return new[] { budget };
            yield break;
        }
        for (int k = 0; k <= budget; k++)
        {
            foreach (var tail in Recurse(dims - 1, budget - k))
            {
                var counts = new int[dims];
                counts[0] = k;
                Array.Copy(tail, 0, counts, 1, dims - 1);
                yield return counts;
            }
        }
    }
}

/// <summary>在网格可行点上按比例空间邻接关系做连通区聚类（多个可行区）。</summary>
public static class Components
{
    public static List<List<int>> Find(IReadOnlyList<double[]> feasible, double eps)
    {
        int n = feasible.Count;
        var dsu = Enumerable.Range(0, n).Select(i => i).ToArray();
        int FindRoot(int x) { while (dsu[x] != x) { dsu[x] = dsu[dsu[x]]; x = dsu[x]; } return x; }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double d2 = 0;
                for (int k = 0; k < feasible[i].Length; k++)
                {
                    double d = feasible[i][k] - feasible[j][k];
                    d2 += d * d;
                }
                if (d2 <= eps * eps * 1.0001) dsu[FindRoot(j)] = FindRoot(i);
            }

        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int r = FindRoot(i);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<int>();
            list.Add(i);
        }
        var result = new List<List<int>>(groups.Values);
        result.Sort((a, b) => b.Count.CompareTo(a.Count));
        return result;
    }
}

/// <summary>2D 凸包及包络外最近点（用于"样品超出包络"判定与残差展示）。</summary>
public static class ConvexHull2
{
    public static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count <= 1) return sorted;
        var lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double X, double Y)>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    public static bool ContainsPolygon(List<(double X, double Y)> poly, (double X, double Y) p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>点到线段的最近点。</summary>
    public static (double X, double Y) ClosestOnSegment((double X, double Y) a, (double X, double Y) b, (double X, double Y) p)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 < 1e-18 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return (a.X + t * dx, a.Y + t * dy);
    }
}

/// <summary>卡方分位数（Wilson–Hilferty 近似），用于 Mahalanobis 可行域门槛。</summary>
public static class ChiSquare
{
    public static double Quantile(double df, double p)
    {
        const double a0 = 2.5066282746310002;
        const double c0 = -1.0;
        const double c1 = 0.2533471031358;
        const double d1 = 0.922847983320086;
        const double d3p1 = -0.1778779974134;
        const double d3 = d3p1 - 1.0;
        const double d2p1 = 1.30740162838758;
        const double d2 = d2p1 - d1;
        double y = p - 0.5;
        double r = y * y;
        double z = y * (c0 + c1 * r) / (1 + d1 * r + d2 * r * r + d3 * r * r * r);
        double t = z;
        double h = 2.0 / (9.0 * df);
        double inner = 1 - h + t * Math.Sqrt(h);
        return df * inner * inner * inner;
    }
}

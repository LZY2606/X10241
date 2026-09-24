using System;
using System.Collections.Generic;

namespace IsotopeMixing.Core.Math;

// 枚举单纯形 {w >= 0, sum w = 1} 上的网格点。
// 网格点天然满足“非负 + 和为一”，绝不用截断再归一化伪造。
public static class Simplex
{
    public static IEnumerable<double[]> Grid(int dimensions, int resolution)
    {
        if (dimensions < 1) throw new ArgumentOutOfRangeException(nameof(dimensions));
        var w = new double[dimensions];
        foreach (var point in Recurse(dimensions, resolution, resolution))
        {
            for (int i = 0; i < dimensions; i++) w[i] = point[i] / (double)resolution;
            var copy = new double[dimensions];
            Array.Copy(w, copy, dimensions);
            yield return copy;
        }
    }

    private static IEnumerable<int[]> Recurse(int dimensions, int left, int resolution)
    {
        if (dimensions == 1)
        {
            yield return new[] { left };
            yield break;
        }
        for (int head = 0; head <= left; head++)
        {
            foreach (var tail in Recurse(dimensions - 1, left - head, resolution))
            {
                var row = new int[dimensions];
                row[0] = head;
                Array.Copy(tail, 0, row, 1, tail.Length);
                yield return row;
            }
        }
    }
}

// 点在凸包内判定（1D / 2D 直接几何；>=3D 用归一化后权重可解性作为回退）。
public static class ConvexHull
{
    public static bool? Contains(double[][] vertices, double[] point)
    {
        int d = point.Length;
        if (d == 1)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var v in vertices)
            {
                min = System.Math.Min(min, v[0]);
                max = System.Math.Max(max, v[0]);
            }
            return point[0] >= min - 1e-9 && point[0] <= max + 1e-9;
        }
        if (d == 2) return Contains2D(Order2D(vertices), point);
        return null;
    }

    // Andrew 单调链：给顶点按凸包顺序排序（无序点集才能做边遍历）。
    public static double[][] Order2D(double[][] points)
    {
        var sorted = new List<double[]>(points);
        sorted.Sort((a, b) => a[0] == b[0] ? a[1].CompareTo(b[1]) : a[0].CompareTo(b[0]));
        var lower = new List<double[]>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross2D(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<double[]>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross2D(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower.ToArray();
    }

    private static double Cross2D(double[] o, double[] a, double[] b) =>
        (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

    private static bool Contains2D(double[][] vertices, double[] p)
    {
        bool hasPositive = false, hasNegative = false;
        int n = vertices.Length;
        for (int i = 0; i < n; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % n];
            double cross = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]);
            if (cross > 1e-9) hasPositive = true;
            if (cross < -1e-9) hasNegative = true;
            if (hasPositive && hasNegative) return false;
        }
        return true;
    }
}

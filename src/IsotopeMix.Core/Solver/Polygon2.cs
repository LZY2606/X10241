using System;
using System.Collections.Generic;
using System.Linq;

namespace IsotopeMix.Core.Solver;

// 二维凸多边形裁剪（Sutherland–Hodgman），工作在比例空间 (p1,p2)，p3=1-p1-p2。
public static class Polygon2
{
    public static readonly double[] V0 = { 0.0, 0.0 }; // 端元 1
    public static readonly double[] V1 = { 1.0, 0.0 }; // 端元 2
    public static readonly double[] V2 = { 0.0, 1.0 }; // 端元 3
    public static readonly List<double[]> Simplex = new() { V0, V1, V2 };

    // 用半平面 n·x <= c 裁剪凸多边形。
    public static List<double[]> Clip(List<double[]> poly, double nx, double ny, double c)
    {
        var output = new List<double[]>();
        if (poly.Count == 0) return output;
        for (int i = 0; i < poly.Count; i++)
        {
            var s = poly[i];
            var e = poly[(i + 1) % poly.Count];
            double ds = nx * s[0] + ny * s[1] - c;
            double de = nx * e[0] + ny * e[1] - c;
            bool sin = ds <= 1e-10;
            bool ein = de <= 1e-10;
            if (sin && ein)
            {
                output.Add(e);
            }
            else if (sin && !ein)
            {
                output.Add(Intersect(s, e, ds, de));
            }
            else if (!sin && ein)
            {
                output.Add(Intersect(s, e, ds, de));
                output.Add(e);
            }
        }
        return Dedup(output);
    }

    static double[] Intersect(double[] s, double[] e, double ds, double de)
    {
        double t = ds / (ds - de);
        return new[] { s[0] + t * (e[0] - s[0]), s[1] + t * (e[1] - s[1]) };
    }

    static List<double[]> Dedup(List<double[]> pts)
    {
        var clean = new List<double[]>();
        foreach (var p in pts)
        {
            if (!clean.Any(q => Math.Abs(q[0] - p[0]) < 1e-9 && Math.Abs(q[1] - p[1]) < 1e-9))
                clean.Add(p);
        }
        return clean;
    }

    // 以重心排序，保证多边形顺序确定（SVG/断言稳定）。
    public static List<double[]> SortByAngle(List<double[]> pts)
    {
        if (pts.Count <= 2) return pts;
        double cx = pts.Average(p => p[0]);
        double cy = pts.Average(p => p[1]);
        return pts
            .OrderBy(p => Math.Atan2(p[1] - cy, p[0] - cx))
            .ToList();
    }

    public static double Area(List<double[]> poly)
    {
        if (poly.Count < 3) return 0.0;
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p[0] * q[1] - q[0] * p[1];
        }
        return Math.Abs(a) / 2.0;
    }

    public static double[] Centroid(List<double[]> poly)
    {
        if (poly.Count == 1) return poly[0];
        double x = poly.Average(p => p[0]);
        double y = poly.Average(p => p[1]);
        return new[] { x, y };
    }
}

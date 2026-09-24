using System;
using System.Collections.Generic;
using System.Linq;

namespace IsotopeMixing.Core.Engine;

/// <summary>
/// 简化坐标空间中的置信椭圆 { y: (y-c)^T H (y-c) <= T }，
/// H = Ar^T Sigma^{-1} Ar，T 为卡方临界值。求它与单纯形/先验多面体的交。
/// </summary>
public sealed class EllipseRegion
{
    public required double[] Center { get; init; }
    public required double[,] H { get; init; }
    public required double Threshold { get; init; }

    public double Value(double[] y)
    {
        var d = y.Zip(Center, (a, b) => a - b).ToArray();
        return Polytope.Inner(d, LinearAlgebra.Multiply(H, d));
    }

    public bool Contains(double[] y, double slack = 0) => Value(y) <= Threshold + slack;

    /// <summary>判定与多面体是否有交：检查多面体顶点与椭圆中心投影，再做边扫描。</summary>
    public bool Intersects(Polytope poly)
    {
        if (poly.Dim == 0) return true;
        if (poly.FeasiblePoint(Center)) return true;
        var projection = poly.Project(Center);
        if (Contains(projection, 1e-8)) return true;
        foreach (var v in poly.Vertices)
            if (Contains(v, 1e-8)) return true;
        // 边/面上二次函数的最小点
        foreach (var pair in PolytopeEdges(poly))
        {
            var m = MinOnSegment(pair.a, pair.b);
            if (m is not null && Contains(m, 1e-8)) return true;
        }
        return false;
    }

    public static IEnumerable<(double[] a, double[] b)> PolytopeEdges(Polytope poly)
    {
        if (poly.Dim == 1)
        {
            foreach (var v in poly.Vertices.OrderBy(p => p[0]).Skip(1).Zip(
                         poly.Vertices.OrderBy(p => p[0])))
                yield return (v.First, v.Second);
            yield break;
        }
        if (poly.Dim == 2)
        {
            var ordered = OrderPolygon2(poly.Vertices);
            for (int i = 0; i < ordered.Count; i++)
                yield return (ordered[i], ordered[(i + 1) % ordered.Count]);
            yield break;
        }
        foreach (var v in poly.Vertices)
            foreach (var w in poly.Vertices)
                if (ReferenceLess(v, w)) yield return (v, w);
    }

    private double[]? MinOnSegment(double[] a, double[] b)
    {
        var dir = b.Zip(a, (bi, ai) => bi - ai).ToArray();
        double d2 = Polytope.Inner(dir, dir);
        if (d2 < 1e-16) return Contains(a, 1e-8) ? a : null;
        var ac = a.Zip(Center, (ai, ci) => ai - ci).ToArray();
        double t = -Polytope.Inner(ac, LinearAlgebra.Multiply(H, dir)) / Polytope.Inner(dir, LinearAlgebra.Multiply(H, dir));
        t = Math.Clamp(t, 0, 1);
        var p = a.Zip(dir, (ai, di) => ai + t * di).ToArray();
        return Contains(p, 1e-8) ? p : null;
    }

    /// <summary>2D：枚举椭圆与多面体交的边界多边形（凸交，顶点来自多边形内部顶点与边界穿点）。</summary>
    public List<double[]> IntersectionPolygon(Polytope poly)
    {
        if (poly.Dim != 2) throw new InvalidOperationException("IntersectionPolygon 仅支持 2D 简化坐标");
        var points = new List<double[]>();
        foreach (var v in poly.Vertices)
            if (Contains(v, 1e-7)) points.Add((double[])v.Clone());
        foreach (var (a, b) in PolytopeEdges(poly))
        {
            var roots = SegmentEllipseRoots(a, b);
            foreach (var t in roots)
            {
                var p = a.Zip(b, (ai, bi) => ai + t * (bi - ai)).ToArray();
                points.Add(p);
            }
        }
        // 椭圆弧上位于多面体内的段：若中心点不在多面体内，弧不会贡献（交=凸集相交）；
        // 当交存在且多边形顶点都在椭圆内/边界时，points 已完整。
        var unique = new List<double[]>();
        foreach (var p in points.OrderBy(p => p[0]).ThenBy(p => p[1]))
            if (!unique.Any(q => Polytope.Dist2(p, q) < 1e-12)) unique.Add(p);
        if (unique.Count < 3 && poly.FeasiblePoint(Center) && Contains(Center))
        {
            // 交被椭圆主导：补椭圆与约束直线的交点已在上面覆盖；此处补采样兜底
            foreach (var p in EllipseBoundarySamples(poly, 96))
                if (!unique.Any(q => Polytope.Dist2(p, q) < 1e-10)) unique.Add(p);
        }
        return OrderPolygon2(unique);
    }

    private List<double[]> EllipseBoundarySamples(Polytope poly, int count)
    {
        var (vals, vecs) = LinearAlgebra.SymmetricEigen(H);
        var res = new List<double[]>();
        for (int k = 0; k < count; k++)
        {
            double theta = 2 * Math.PI * k / count;
            var y = new double[2];
            for (int j = 0; j < 2; j++)
            {
                double r = Math.Sqrt(Threshold / Math.Max(vals[j], 1e-12));
                for (int i = 0; i < 2; i++)
                    y[i] += Center[i] + vecs[i, j] * r * Math.Cos(theta) - Center[i];
            }
            for (int i = 0; i < 2; i++)
            {
                double s = 0;
                for (int j = 0; j < 2; j++)
                {
                    double r = Math.Sqrt(Threshold / Math.Max(vals[j], 1e-12));
                    s += vecs[i, j] * r * (j == 0 ? Math.Cos(theta) : Math.Sin(theta));
                }
                y[i] = Center[i] + s;
            }
            if (poly.FeasiblePoint(y)) res.Add(y);
        }
        return res;
    }

    private List<double> SegmentEllipseRoots(double[] a, double[] b)
    {
        var dir = b.Zip(a, (bi, ai) => bi - ai).ToArray();
        var ac = a.Zip(Center, (ai, ci) => ai - ci).ToArray();
        var hd = LinearAlgebra.Multiply(H, dir);
        double qa = Polytope.Inner(dir, hd);
        double qb = 2 * Polytope.Inner(ac, hd);
        double qc = Polytope.Inner(ac, LinearAlgebra.Multiply(H, ac)) - Threshold;
        var roots = new List<double>();
        if (Math.Abs(qa) < 1e-16)
        {
            if (Math.Abs(qb) > 1e-16)
            {
                double t = -qc / qb;
                if (t >= -1e-9 && t <= 1 + 1e-9) roots.Add(Math.Clamp(t, 0, 1));
            }
            return roots;
        }
        double disc = qb * qb - 4 * qa * qc;
        if (disc < -1e-9) return roots;
        disc = Math.Max(0, disc);
        double sq = Math.Sqrt(disc);
        foreach (var t in new[] { (-qb - sq) / (2 * qa), (-qb + sq) / (2 * qa) })
            if (t >= -1e-9 && t <= 1 + 1e-9) roots.Add(Math.Clamp(t, 0, 1));
        return roots;
    }

    public static List<double[]> OrderPolygon2(List<double[]> pts)
    {
        if (pts.Count <= 2) return pts.Select(p => (double[])p.Clone()).ToList();
        double cx = pts.Average(p => p[0]);
        double cy = pts.Average(p => p[1]);
        return pts.OrderBy(p => Math.Atan2(p[1] - cy, p[0] - cx)).Select(p => (double[])p.Clone()).ToList();
    }

    private static bool ReferenceLess(double[] a, double[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i])) return a[i] < b[i];
        return false;
    }
}

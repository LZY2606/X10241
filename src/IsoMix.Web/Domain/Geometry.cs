namespace IsoMix.Domain;

public static class Geometry
{
    // Monotonic chain convex hull; returns hull vertices counter-clockwise.
    public static double[][] ConvexHull(IReadOnlyList<double[]> points)
    {
        var pts = points.Select(p => (double[])p.Clone())
            .OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
        if (pts.Count <= 1) return pts.ToArray();
        var distinct = new List<double[]>();
        foreach (var p in pts)
            if (distinct.Count == 0 || Math.Abs(distinct[^1][0] - p[0]) > 1e-12 || Math.Abs(distinct[^1][1] - p[1]) > 1e-12)
                distinct.Add(p);
        if (distinct.Count <= 2) return distinct.ToArray();
        double Cross(double[] o, double[] a, double[] b) =>
            (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
        var lower = new List<double[]>();
        foreach (var p in distinct)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<double[]>();
        for (int i = distinct.Count - 1; i >= 0; i--)
        {
            var p = distinct[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower.ToArray();
    }

    public static bool PointInPolygon(double[] point, IReadOnlyList<double[]> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a[1] > point[1]) != (b[1] > point[1]) &&
                point[0] < (b[0] - a[0]) * (point[1] - a[1]) / (b[1] - a[1]) + a[0])
                inside = !inside;
        }
        return inside;
    }

    // Sutherland-Hodgman: clip subject polygon by convex clip polygon (CCW).
    public static List<double[]> ClipPolygon(IReadOnlyList<double[]> subject, IReadOnlyList<double[]> clip)
    {
        var output = subject.Select(p => (double[])p.Clone()).ToList();
        for (int i = 0; i < clip.Count; i++)
        {
            var a = clip[i];
            var b = clip[(i + 1) % clip.Count];
            var input = output;
            output = new List<double[]>();
            if (input.Count == 0) break;
            double Side(double[] p) => (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]);
            var s = input[^1];
            double sideS = Side(s);
            foreach (var e in input)
            {
                double sideE = Side(e);
                if (sideE >= -1e-12)
                {
                    if (sideS < -1e-12) output.Add(Intersection(s, e, a, b));
                    output.Add(e);
                }
                else if (sideS >= -1e-12)
                {
                    output.Add(Intersection(s, e, a, b));
                }
                s = e; sideS = sideE;
            }
        }
        return output;
    }

    static double[] Intersection(double[] p1, double[] p2, double[] a, double[] b)
    {
        double d1x = p2[0] - p1[0], d1y = p2[1] - p1[1];
        double d2x = b[0] - a[0], d2y = b[1] - a[1];
        double denom = d1x * d2y - d1y * d2x;
        if (Math.Abs(denom) < 1e-300) return (double[])p1.Clone();
        double t = ((a[0] - p1[0]) * d2y - (a[1] - p1[1]) * d2x) / denom;
        return new[] { p1[0] + t * d1x, p1[1] + t * d1y };
    }

    // Intersect half-planes a.x <= b, starting from a large box. Returns CCW polygon.
    public static List<double[]> HalfPlaneIntersection(IReadOnlyList<(double[] A, double B)> halfPlanes, double boxExtent)
    {
        var poly = new List<double[]>
        {
            new[] { -boxExtent, -boxExtent }, new[] { boxExtent, -boxExtent },
            new[] { boxExtent, boxExtent }, new[] { -boxExtent, boxExtent }
        };
        foreach (var (a, b) in halfPlanes)
        {
            double norm = Math.Sqrt(a[0] * a[0] + (a.Length > 1 ? a[1] * a[1] : 0));
            if (norm < 1e-300) continue;
            // Build a large CCW square representing the half-plane a.x <= b.
            var n = new[] { a[0] / norm, a.Length > 1 ? a[1] / norm : 0 };
            double c = b / norm;
            double m = boxExtent * 4;
            // boundary line: n.x = c; interior is -n direction
            var p1 = new[] { n[0] * c - n[1] * m, n[1] * c + n[0] * m };
            var p2 = new[] { n[0] * c + n[1] * m, n[1] * c - n[0] * m };
            var p3 = new[] { p2[0] - n[0] * 2 * m, p2[1] - n[1] * 2 * m };
            var p4 = new[] { p1[0] - n[0] * 2 * m, p1[1] - n[1] * 2 * m };
            // ensure CCW
            var half = new List<double[]> { p1, p2, p3, p4 };
            if (SignedArea(half) < 0) half.Reverse();
            poly = ClipPolygon(poly, half);
            if (poly.Count == 0) break;
        }
        return poly;
    }

    public static double SignedArea(IReadOnlyList<double[]> poly)
    {
        double s = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            s += a[0] * b[1] - b[0] * a[1];
        }
        return 0.5 * s;
    }

    // Approximate ellipse (x-c)^T H (x-c) <= level as a polygon (H 2x2 posdef-ish).
    public static List<double[]> EllipsePolygon(double[] center, double[][] h, double level, int segments, double maxRadius)
    {
        var (values, vectors) = LinAlg.EigenSymmetric(h);
        var pts = new List<double[]>();
        for (int i = 0; i < segments; i++)
        {
            double theta = 2 * Math.PI * i / segments;
            double x = 0, y = 0;
            for (int k = 0; k < 2; k++)
            {
                double lambda = Math.Max(values[k], level / (maxRadius * maxRadius));
                double radius = Math.Min(Math.Sqrt(level / lambda), maxRadius);
                double comp = k == 0 ? Math.Cos(theta) : Math.Sin(theta);
                x += vectors[k][0] * comp * radius;
                y += vectors[k][1] * comp * radius;
            }
            pts.Add(new[] { center[0] + x, center[1] + y });
        }
        if (SignedArea(pts) < 0) pts.Reverse();
        return pts;
    }
}

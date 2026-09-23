namespace IsoMix;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;
    public double Norm() => Math.Sqrt(X * X + Y * Y);
}

/// <summary>Half-plane { p : Normal·p &lt;= Offset }.</summary>
public readonly record struct HalfPlane(Vec2 Normal, double Offset)
{
    public double Eval(Vec2 p) => Normal.Dot(p) - Offset;
}

public static class Geo
{
    public const double Eps = 1e-9;

    /// <summary>Sutherland–Hodgman clip of a convex polygon by one half-plane.</summary>
    public static List<Vec2> ClipHalfPlane(IReadOnlyList<Vec2> poly, HalfPlane hp)
    {
        var result = new List<Vec2>();
        int n = poly.Count;
        if (n == 0) return result;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            double da = hp.Eval(a), db = hp.Eval(b);
            bool ina = da <= Eps, inb = db <= Eps;
            if (ina) result.Add(a);
            if (ina != inb)
            {
                double t = da / (da - db);
                result.Add(a + (b - a) * t);
            }
        }
        return result;
    }

    public static List<Vec2> ClipAll(IReadOnlyList<Vec2> poly, IEnumerable<HalfPlane> planes)
    {
        var current = poly.ToList();
        foreach (var hp in planes)
        {
            current = ClipHalfPlane(current, hp);
            if (current.Count == 0) break;
        }
        return current;
    }

    /// <summary>The 2-simplex in (f1, f2) coordinates with f3 = 1 - f1 - f2.</summary>
    public static List<Vec2> SimplexTriangle() =>
        new() { new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1) };

    /// <summary>Monotone-chain convex hull, counter-clockwise, no repeated closing point.</summary>
    public static List<Vec2> ConvexHull(IReadOnlyList<Vec2> pts)
    {
        var sorted = pts.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count <= 2) return sorted;
        List<Vec2> lower = new(), upper = new();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && (lower[^1] - lower[^2]).Cross(p - lower[^1]) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && (upper[^1] - upper[^2]).Cross(p - upper[^1]) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    public static bool PointInConvex(IReadOnlyList<Vec2> hullCcw, Vec2 p, double eps = 1e-9)
    {
        int n = hullCcw.Count;
        if (n < 3) return false;
        for (int i = 0; i < n; i++)
        {
            var a = hullCcw[i];
            var b = hullCcw[(i + 1) % n];
            if ((b - a).Cross(p - a) < -eps) return false;
        }
        return true;
    }

    public static double SignedArea(IReadOnlyList<Vec2> poly)
    {
        double s = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2;
    }

    public static double Area(IReadOnlyList<Vec2> poly) => Math.Abs(SignedArea(poly));

    public static Vec2 Centroid(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count == 0) return new Vec2(0, 0);
        double a = SignedArea(poly);
        if (Math.Abs(a) < 1e-12)
        {
            return new Vec2(poly.Average(p => p.X), poly.Average(p => p.Y));
        }
        double cx = 0, cy = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            double w = p.X * q.Y - q.X * p.Y;
            cx += (p.X + q.X) * w;
            cy += (p.Y + q.Y) * w;
        }
        return new Vec2(cx / (6 * a), cy / (6 * a));
    }

    /// <summary>
    /// Boundary polygon (inscribed) of the ellipse { f : (f-c)ᵀ P (f-c) &lt;= level }
    /// for a symmetric positive-definite 2x2 matrix P.
    /// </summary>
    public static List<Vec2> EllipseBoundary(Vec2 center, double pxx, double pxy, double pyy, double level, int segments = 128)
    {
        double tr = pxx + pyy;
        double det = pxx * pyy - pxy * pxy;
        double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc;
        double l2 = tr / 2 - disc;
        if (l2 <= 0) return new List<Vec2>();
        Vec2 e2;
        if (Math.Abs(pxy) > 1e-12)
        {
            e2 = new Vec2(pxy, l2 - pxx);
        }
        else
        {
            e2 = pxx <= pyy ? new Vec2(1, 0) : new Vec2(0, 1);
        }
        e2 = e2 * (1.0 / e2.Norm());
        var e1 = new Vec2(-e2.Y, e2.X);
        double r1 = Math.Sqrt(level / l1);
        double r2 = Math.Sqrt(level / l2);
        var pts = new List<Vec2>(segments);
        for (int i = 0; i < segments; i++)
        {
            double t = 2 * Math.PI * i / segments;
            pts.Add(center + e1 * (r1 * Math.Cos(t)) + e2 * (r2 * Math.Sin(t)));
        }
        return pts;
    }
}

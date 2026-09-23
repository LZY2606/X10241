namespace IsotopeLab.Mixing;

public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double s) => new(a.X * s, a.Y * s);
}

public static class Geometry
{
    /// <summary>Eigenvalues of a symmetric matrix (Jacobi iteration). Used for singular-value ratios.</summary>
    public static double[] SymmetricEigenvalues(double[,] aIn)
    {
        var n = aIn.GetLength(0);
        var a = (double[,])aIn.Clone();
        for (var sweep = 0; sweep < 100; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < n - 1; p++)
                for (var q = p + 1; q < n; q++)
                    off += a[p, q] * a[p, q];
            if (off < 1e-20) break;
            for (var p = 0; p < n - 1; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (Math.Abs(a[p, q]) < 1e-15) continue;
                var tau = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                var t = tau >= 0 ? 1.0 / (tau + Math.Sqrt(1 + tau * tau))
                                 : -1.0 / (-tau + Math.Sqrt(1 + tau * tau));
                var c = 1 / Math.Sqrt(1 + t * t);
                var s = t * c;
                for (var k = 0; k < n; k++)
                {
                    var akp = a[k, p];
                    var akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (var k = 0; k < n; k++)
                {
                    var apk = a[p, k];
                    var aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
            }
        }
        var vals = new double[n];
        for (var i = 0; i < n; i++) vals[i] = a[i, i];
        Array.Sort(vals);
        Array.Reverse(vals);
        return vals;
    }

    public static double PolygonArea(IReadOnlyList<Pt> poly)
    {
        if (poly.Count < 3) return 0;
        double sum = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
    }

    public static Pt Centroid(IReadOnlyList<Pt> poly)
    {
        if (poly.Count == 0) return new Pt(0, 0);
        double x = 0, y = 0;
        foreach (var p in poly) { x += p.X; y += p.Y; }
        return new Pt(x / poly.Count, y / poly.Count);
    }

    /// <summary>Sutherland-Hodgman clip against the half-plane n·p ≤ c.</summary>
    public static List<Pt> ClipHalfPlane(IReadOnlyList<Pt> poly, double nx, double ny, double c)
    {
        var output = new List<Pt>();
        if (poly.Count == 0) return output;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var da = nx * a.X + ny * a.Y - c;
            var db = nx * b.X + ny * b.Y - c;
            var insideA = da <= 1e-10;
            var insideB = db <= 1e-10;
            if (insideA && insideB)
            {
                output.Add(b);
            }
            else if (insideA && !insideB)
            {
                var t = da / (da - db);
                output.Add(new Pt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            }
            else if (!insideA && insideB)
            {
                var t = da / (da - db);
                output.Add(new Pt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                output.Add(b);
            }
        }
        return RemoveDups(output);
    }

    public static List<Pt> RemoveDups(List<Pt> poly)
    {
        var result = new List<Pt>();
        foreach (var p in poly)
        {
            if (result.Count == 0) { result.Add(p); continue; }
            var last = result[^1];
            if (Math.Abs(last.X - p.X) > 1e-9 || Math.Abs(last.Y - p.Y) > 1e-9) result.Add(p);
        }
        if (result.Count > 1)
        {
            var first = result[0];
            var last = result[^1];
            if (Math.Abs(first.X - last.X) <= 1e-9 && Math.Abs(first.Y - last.Y) <= 1e-9)
                result.RemoveAt(result.Count - 1);
        }
        return result;
    }

    /// <summary>Weights for a point in (f1..f_{k-1}) space; f_k is the remainder.</summary>
    public static double[] Weights(Pt p, int k)
    {
        var f = new double[k];
        f[0] = p.X;
        if (k >= 3) f[1] = p.Y;
        var assigned = 0.0;
        for (var r = 0; r < k - 1; r++) assigned += f[r];
        f[k - 1] = 1 - assigned;
        return f;
    }

    /// <summary>Ternary-diagram coordinates for 3 sources: s1=(0,0), s2=(1,0), s3=(0.5,√3/2).</summary>
    public static Pt ToTernary(double[] f) =>
        new(f[1] + 0.5 * f[2], Math.Sqrt(3) / 2 * f[2]);

    public static double IntervalsOverlapLength((double lo, double hi) a, (double lo, double hi) b)
    {
        var lo = Math.Max(a.lo, b.lo);
        var hi = Math.Min(a.hi, b.hi);
        return Math.Max(0, hi - lo);
    }
}

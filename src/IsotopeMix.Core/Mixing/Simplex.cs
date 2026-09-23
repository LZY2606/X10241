using System;

namespace IsotopeMix.Core.Mixing;

/// <summary>
/// Projection onto { p : sum(p)=1, lower_i &lt;= p_i &lt;= upper_i }.
/// All mixing constraints are enforced by genuine constrained projection — never by
/// truncating negatives to zero and renormalising, which fabricates the answer.
/// </summary>
public static class Simplex
{
    public static double[] Project(double[] y, double[]? lower = null, double[]? upper = null)
    {
        var n = y.Length;
        var lo = new double[n];
        var hi = new double[n];
        for (var i = 0; i < n; i++)
        {
            lo[i] = lower?[i] ?? 0.0;
            hi[i] = upper?[i] ?? 1.0;
            if (hi[i] < lo[i]) hi[i] = lo[i];
        }
        // Find lambda so that clamp(y_i - lambda, lo_i, hi_i) sums to 1, by bisection.
        var loBound = double.PositiveInfinity;
        var hiBound = double.NegativeInfinity;
        for (var i = 0; i < n; i++)
        {
            loBound = System.Math.Min(loBound, y[i] - hi[i]);
            hiBound = System.Math.Max(hiBound, y[i] - lo[i]);
        }
        for (var iter = 0; iter < 200; iter++)
        {
            var mid = (loBound + hiBound) / 2.0;
            var sum = 0.0;
            for (var i = 0; i < n; i++) sum += Clamp(y[i] - mid, lo[i], hi[i]);
            if (sum > 1.0) loBound = mid;
            else hiBound = mid;
        }
        var p = new double[n];
        for (var i = 0; i < n; i++) p[i] = Clamp(y[i] - (loBound + hiBound) / 2.0, lo[i], hi[i]);
        // Guard against floating point drift.
        var s = Sum(p);
        if (System.Math.Abs(s - 1.0) > 1e-10)
        {
            for (var i = 0; i < n; i++) p[i] = lo[i] + (p[i] - lo[i]) / (s - Sum(lo) + double.Epsilon) * (1.0 - Sum(lo));
        }
        return p;
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    private static double Sum(double[] a) { var s = 0.0; foreach (var v in a) s += v; return s; }
}

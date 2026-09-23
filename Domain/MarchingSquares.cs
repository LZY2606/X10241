using System;
using System.Collections.Generic;
using System.Linq;

namespace IsoMix.Domain;

/// <summary>
/// 在单纯形网格上提取 q &lt;= threshold 区域（每个连通分量）的等值边界折线。
/// 输出为 (a,b) 平面上的闭合环；多个分量分别提取，分离的子轮廓各自成环。
/// </summary>
public static class MarchingSquares
{
    private const double Eps = 1e-9;

    public static List<double[][]> BuildLoops(double[] f1, double[] f2, double[,] q, double threshold,
        List<(int i, int j)> comp, double[] priLo, double[] priHi)
    {
        var inComp = new HashSet<(int, int)>(comp);
        int minI = comp.Min(p => p.i), maxI = comp.Max(p => p.i);
        int minJ = comp.Min(p => p.j), maxJ = comp.Max(p => p.j);

        var segments = new List<(double[] p, double[] q2)>();
        for (int i = minI; i < maxI; i++)
            for (int j = minJ; j < maxJ; j++)
            {
                if (!inComp.Contains((i, j)) && !inComp.Contains((i + 1, j)) &&
                    !inComp.Contains((i, j + 1)) && !inComp.Contains((i + 1, j + 1)))
                    continue;

                double v00 = At(q, i, j), v10 = At(q, i + 1, j), v11 = At(q, i + 1, j + 1), v01 = At(q, i, j + 1);
                bool b00 = In(threshold, v00), b10 = In(threshold, v10), b11 = In(threshold, v11), b01 = In(threshold, v01);
                int code = (b00 ? 1 : 0) | (b10 ? 2 : 0) | (b11 ? 4 : 0) | (b01 ? 8 : 0);
                if (code is 0 or 15) continue;

                double[] Btm() => Pt(f1[i] + Interp(v00, v10, threshold, f1[i], f1[i + 1]), f2[j]);
                double[] Rgt() => Pt(f1[i + 1], f2[j] + Interp(v10, v11, threshold, f2[j], f2[j + 1]));
                double[] Top() => Pt(f1[i] + Interp(v01, v11, threshold, f1[i], f1[i + 1]), f2[j + 1]);
                double[] Lft() => Pt(f1[i], f2[j] + Interp(v00, v01, threshold, f2[j], f2[j + 1]));

                void Add(int a, int b)
                {
                    double[] s = a switch { 0 => Btm(), 1 => Rgt(), 2 => Top(), _ => Lft() };
                    double[] e = b switch { 0 => Btm(), 1 => Rgt(), 2 => Top(), _ => Lft() };
                    if (!Same(s, e)) segments.Add((s, e));
                }

                switch (code)
                {
                    case 1: case 14: Add(0, 3); break;
                    case 2: case 13: Add(0, 1); break;
                    case 3: case 12: Add(1, 3); break;
                    case 4: case 11: Add(1, 2); break;
                    case 6: case 9: Add(0, 2); break;
                    case 7: case 8: Add(2, 3); break;
                    // 鞍点：用分量归属消歧
                    case 5:
                        if (b00 == b11) { Add(0, 3); Add(1, 2); } else { Add(0, 1); Add(2, 3); }
                        break;
                    case 10:
                        if (b00 == b11) { Add(0, 1); Add(2, 3); } else { Add(0, 3); Add(1, 2); }
                        break;
                }
            }

        return Stitch(segments);
    }

    private static double At(double[,] q, int i, int j)
    {
        double v = q[i, j];
        return double.IsInfinity(v) ? 1e30 : v;
    }

    private static bool In(double t, double v) => v <= t + Eps && !double.IsInfinity(v);

    private static double[] Pt(double a, double b) => new[] { a, b };

    private static double Interp(double va, double vb, double t, double xa, double xb)
    {
        double denom = vb - va;
        if (Math.Abs(denom) < 1e-15) return xa;
        return xa + (t - va) / denom * (xb - xa);
    }

    private static bool Same(double[] p, double[] q2) =>
        Math.Abs(p[0] - q2[0]) < 1e-12 && Math.Abs(p[1] - q2[1]) < 1e-12;

    private static List<double[][]> Stitch(List<(double[] p, double[] q)> segs)
    {
        var loops = new List<double[][]>();
        var remaining = new List<(double[] p, double[] q)>(segs);
        double tol = 1.5 / MixingMath.GridN;
        while (remaining.Count > 0)
        {
            var loop = new List<double[]>();
            var cur = remaining[0]; remaining.RemoveAt(0);
            loop.Add(cur.p); loop.Add(cur.q);
            bool grew = true;
            int guard = remaining.Count + 2;
            while (grew && guard-- > 0)
            {
                grew = false;
                var head = loop[0]; var tail = loop[^1];
                if (Near(head, tail, tol) && loop.Count > 3) break;
                for (int idx = 0; idx < remaining.Count; idx++)
                {
                    var s = remaining[idx];
                    if (Near(s.p, tail, tol)) { loop.Add(s.q); remaining.RemoveAt(idx); grew = true; break; }
                    if (Near(s.q, tail, tol)) { loop.Add(s.p); remaining.RemoveAt(idx); grew = true; break; }
                    if (Near(s.p, head, tol)) { loop.Insert(0, s.q); remaining.RemoveAt(idx); grew = true; break; }
                    if (Near(s.q, head, tol)) { loop.Insert(0, s.p); remaining.RemoveAt(idx); grew = true; break; }
                }
            }
            if (Near(loop[0], loop[^1], tol)) loop.RemoveAt(loop.Count - 1);
            loops.Add(loop.ToArray());
        }
        return loops;
    }

    private static bool Near(double[] p, double[] q, double tol) =>
        Math.Abs(p[0] - q[0]) <= tol && Math.Abs(p[1] - q[1]) <= tol;
}

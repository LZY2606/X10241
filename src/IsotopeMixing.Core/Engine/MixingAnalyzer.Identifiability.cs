using System;
using System.Collections.Generic;
using System.Linq;

namespace IsotopeMixing.Core.Engine;

public sealed partial class MixingAnalyzer
{
    private static Identifiability AssessIdentifiability(
        double[,] effectiveEndmembers,
        double[] observed,
        double[,]? cov,
        int n,
        int m,
        List<Judgment> shared,
        string sampleId)
    {
        int d = m - 1;

        // 缺失同位素维：观测维数不足以唯一确定自由度
        if (n < d)
        {
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.UnderdeterminedMissingDimension,
                Severity = "warning",
                Message = $"只有 {n} 个同位素维而有 {m} 个端元（自由度 {d}），比例无法唯一决定；保留全部合法候选区间。",
                Metrics = new Dictionary<string, double?> { ["observedDimensions"] = n, ["freeProportions"] = d },
                RefObjectId = sampleId
            });
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.NonUniqueCandidatesRetained,
                Severity = "info",
                Message = "未做截断后归一化；保留满足约束与置信水平的全部合法候选。",
                Metrics = new Dictionary<string, double?>()
            });
            return Identifiability.Unidentifiable;
        }

        // 白化端元几何：按测量尺度加权后的最小奇异值
        var w = cov is null ? IdentityScale(n) : Whitening(cov);
        var aw = new double[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                double s = 0;
                for (int t = 0; t < n; t++) s += w[i, t] * effectiveEndmembers[j, t];
                aw[i, j] = s;
            }
        var svd = ThinSvd.Decompose(aw);
        double smin = svd.SingularValues[Math.Min(d, svd.SingularValues.Length - 1)];
        double smax = svd.SingularValues[0];
        double cond = smax / Math.Max(smin, 1e-300);

        if (smin <= RankDeficientSmin)
        {
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.EndmembersNearCollinear,
                Severity = "error",
                Message = "端元在白化同位素空间中严格共线/秩亏，比例不可识别，输出可行域而非单点比例。",
                Metrics = new Dictionary<string, double?> { ["whitenedSmin"] = smin, ["conditionNumber"] = cond, ["threshold"] = RankDeficientSmin },
            });
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.NonUniqueCandidatesRetained,
                Severity = "info",
                Message = "近共线情形下全部合法候选保留，不输出唯一比例，不做截断后归一化。",
                Metrics = new Dictionary<string, double?>()
            });
            return Identifiability.Unidentifiable;
        }
        if (smin < NearCollinearSmin)
        {
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.EndmembersNearCollinear,
                Severity = "warning",
                Message = $"端元在白化同位素空间中近共线（最小奇异值 {smin:F3} < {NearCollinearSmin}，条件数 {cond:F1}），可识别性不足。",
                Metrics = new Dictionary<string, double?> { ["whitenedSmin"] = smin, ["conditionNumber"] = cond, ["threshold"] = NearCollinearSmin },
            });
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.NonUniqueCandidatesRetained,
                Severity = "info",
                Message = "近共线情形下全部合法候选保留，不输出唯一比例，不做截断后归一化。",
                Metrics = new Dictionary<string, double?>()
            });
            return Identifiability.WeaklyIdentified;
        }
        return Identifiability.WellIdentified;
    }

    private static double[,] IdentityScale(int n)
    {
        var id = new double[n, n];
        for (int i = 0; i < n; i++) id[i, i] = 1;
        return id;
    }

    /// <summary>白化矩阵 W = Sigma^{-1/2}（对称），使 W Sigma W^T = I。</summary>
    private static double[,] Whitening(double[,] cov)
    {
        var (vals, vecs) = LinearAlgebra.SymmetricEigen(cov);
        int n = cov.GetLength(0);
        var w = new double[n, n];
        for (int j = 0; j < n; j++)
        {
            double inv = 1.0 / Math.Sqrt(Math.Max(vals[j], 1e-12));
            for (int i = 0; i < n; i++) w[i, j] = vecs[i, j] * inv;
        }
        return LinearAlgebra.Multiply(w, LinearAlgebra.Transpose(vecs));
    }

    private static bool DetectDisjoint(IReadOnlyList<CandidateSolution> candidates)
    {
        var feasible = candidates.Where(c => c.Region.Outcome == FeasibilityOutcome.NonEmpty).ToList();
        if (feasible.Count < 2) return false;
        // 2D：多边形包含关系/相交检测；其余维度用代表点是否落在对方区间内判定
        int disjointPairs = 0, pairs = 0;
        for (int i = 0; i < feasible.Count; i++)
            for (int j = i + 1; j < feasible.Count; j++)
            {
                pairs++;
                if (!RegionsOverlap(feasible[i].Region, feasible[j].Region)) disjointPairs++;
            }
        return pairs > 0 && disjointPairs == pairs;
    }

    private static bool RegionsOverlap(FeasibleRegion a, FeasibleRegion b)
    {
        if (a.Polygon is not null && b.Polygon is not null)
            return PolygonsOverlap(a.Polygon, b.Polygon);
        return IntervalOverlap(a, b);
    }

    private static bool IntervalOverlap(FeasibleRegion a, FeasibleRegion b)
    {
        if (a.MinProportions is null || b.MinProportions is null) return true;
        for (int k = 0; k < a.MinProportions.Length; k++)
            if (a.MaxProportions![k] < b.MinProportions[k] - 1e-7 || b.MaxProportions![k] < a.MinProportions[k] - 1e-7)
                return false;
        return true;
    }

    private static bool PolygonsOverlap(Polygon2 a, Polygon2 b)
    {
        // 任一顶点在对方内部，或边相交
        foreach (var p in a.Vertices) if (PointInPolygon(p, b)) return true;
        foreach (var p in b.Vertices) if (PointInPolygon(p, a)) return true;
        for (int i = 0; i < a.Vertices.Count; i++)
            for (int j = 0; j < b.Vertices.Count; j++)
                if (SegmentsIntersect(a.Vertices[i], a.Vertices[(i + 1) % a.Vertices.Count],
                                      b.Vertices[j], b.Vertices[(j + 1) % b.Vertices.Count]))
                    return true;
        return false;
    }

    private static bool PointInPolygon(double[] p, Polygon2 poly)
    {
        bool inside = false;
        var verts = poly.Vertices;
        for (int i = 0, j = verts.Count - 1; i < verts.Count; j = i++)
        {
            var vi = verts[i]; var vj = verts[j];
            if (((vi[1] > p[1]) != (vj[1] > p[1]))
                && p[0] < (vj[0] - vi[0]) * (p[1] - vi[1]) / (vj[1] - vi[1] + 1e-300) + vi[0])
                inside = !inside;
        }
        return inside;
    }

    private static bool SegmentsIntersect(double[] p1, double[] p2, double[] p3, double[] p4)
    {
        double d1 = Cross(p4[0] - p3[0], p4[1] - p3[1], p1[0] - p3[0], p1[1] - p3[1]);
        double d2 = Cross(p4[0] - p3[0], p4[1] - p3[1], p2[0] - p3[0], p2[1] - p3[1]);
        double d3 = Cross(p2[0] - p1[0], p2[1] - p1[1], p3[0] - p1[0], p3[1] - p1[1]);
        double d4 = Cross(p2[0] - p1[0], p2[1] - p1[1], p4[0] - p1[0], p4[1] - p1[1]);
        return (d1 * d2 < 0 && d3 * d4 < 0);
    }

    private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;
}

using System;
using System.Collections.Generic;
using System.Linq;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Engine;

public sealed partial class MixingAnalyzer
{
    private static FeasibleRegion AnalyzeHypothesis(
        PreparedInputs prepared,
        double[,] cov,
        FractionationHypothesis hypothesis,
        List<(int idx, double min, double max)>? bounds,
        double confidence,
        IReadOnlyDictionary<string, FractionationTable> tables)
    {
        int n = prepared.ObservedTracers.Length;
        int m = prepared.EndmemberOrder.Count;
        int d = m - 1;
        var judgments = new List<Judgment>();

        double[,] eff = EffectiveEndmemberMatrix(prepared, hypothesis, tables);
        double[] em0 = Enumerable.Range(0, n).Select(j => eff[0, j]).ToArray();
        // 简化坐标系数 Ar[:,k] = em[k+1] - em[0]
        var ar = new double[n, d];
        for (int k = 0; k < d; k++)
            for (int j = 0; j < n; j++)
                ar[j, k] = eff[k + 1, j] - em0[j];
        // z = y_obs - em0
        var z = prepared.Observed.Zip(em0, (o, e) => o - e).ToArray();

        var cinv = LinearAlgebra.InvertSPD(cov);
        var h = LinearAlgebra.Multiply(LinearAlgebra.Transpose(ar), LinearAlgebra.Multiply(cinv, ar));
        var g = LinearAlgebra.Multiply(LinearAlgebra.Transpose(ar), LinearAlgebra.Multiply(cinv, z));
        double chi0 = Quad(z, cinv);

        // 先验多面体
        var poly = Polytope.Build(m, bounds);
        double threshold = ChiSquare.CriticalValue(confidence, Math.Max(n, 1));
        // 椭圆中心（无约束）：H c = g
        var ellipse = SafeEllipse(h, g, threshold, d);

        bool priorInfeasible = PriorInfeasible(poly, m);
        if (priorInfeasible)
        {
            judgments.Add(new Judgment
            {
                Code = JudgmentCodes.PriorInfeasible,
                Severity = "error",
                Message = "先验区间与“比例非负、和为一”矛盾（未做截断/归一化补救），该假设下无合法比例。",
                Metrics = new Dictionary<string, double?>(),
                RefObjectId = hypothesis.HypothesisId
            });
            return Region(FeasibilityOutcome.PriorInfeasible, hypothesis, prepared, null, null, null, null, threshold, null, judgments);
        }

        bool intersects = ellipse.Intersects(poly);
        // 超出包络判定（在观测同位素空间）
        bool outside = n >= 2 && OutsideConvexHull(prepared, eff, cinv);
        if (outside)
            judgments.Add(new Judgment
        {
            Code = JudgmentCodes.SampleOutsideEnvelope,
            Severity = intersects ? "info" : "error",
            Message = intersects
                ? "样品点落在端元凸包之外，但在考虑测量与端元协方差后仍存在可行比例区域。"
                : "样品（含其置信椭圆）落在端元凸包之外，任何非负、和为一的比例都无法解释观测。",
                Metrics = new Dictionary<string, double?>(),
                RefObjectId = prepared.EndmemberOrder.FirstOrDefault()
        });

        judgments.Add(new Judgment
        {
            Code = JudgmentCodes.SimplexConstraintsEnforced,
            Severity = "info",
            Message = "比例通过单纯形不等式约束（f_k≥0 且 Σf_k=1）求解，未使用截断后归一化。",
            Metrics = new Dictionary<string, double?> { ["endmemberCount"] = m }
        });

        if (!intersects)
        {
            judgments.Add(new Judgment
            {
                Code = JudgmentCodes.FeasibleRegionEmpty,
                Severity = "error",
                Message = $"在置信水平 {confidence:P0} 下，置信椭圆与比例单纯形/先验多面体无交，可行域为空。",
                Metrics = new Dictionary<string, double?> { ["chiSquareThreshold"] = threshold }
            });
            // 仍给出最接近的代表点（投影），但明确不是可行解
            var closest = poly.MinimizeQuadratic(h, Neg(g));
            double[]? closestF = ToProportions(closest);
            var closestResid = Residuals(closestF, prepared, eff, cov, out double chiAt, out var resTracers);
            return Region(FeasibilityOutcome.Empty, hypothesis, prepared, closestF, null, null, null, threshold,
                closestResid, judgments, chiAt, resTracers);
        }

        // 可行域几何
        Polygon2? polygon = null;
        double[]? mins = null, maxs = null;
        if (d == 2)
        {
            var verts = ellipse.IntersectionPolygon(poly);
            polygon = new Polygon2 { Vertices = verts, ProportionOrder = prepared.EndmemberOrder.ToArray() };
            var (a, b) = BoundsFromPolygon(verts);
            mins = a; maxs = b;
        }
        else
        {
            var (a, b) = BoundsFromEllipse(poly, ellipse);
            mins = a; maxs = b;
        }

        // 代表点：可行域内使卡方最小（不伪称唯一）
        var yOpt = poly.MinimizeQuadratic(h, Neg(g));
        var fOpt = ToProportions(yOpt);
        var resid = Residuals(fOpt, prepared, eff, cov, out double chi, out var tracerOrder);
        judgments.Add(new Judgment
        {
            Code = JudgmentCodes.FeasibleRegionNonEmpty,
            Severity = "info",
            Message = "存在合法可行比例区域；给出的是区域代表点（最小卡方），不是唯一答案。",
            Metrics = new Dictionary<string, double?> { ["chiSquareAtRepresentative"] = chi, ["chiSquareThreshold"] = threshold }
        });
        return Region(FeasibilityOutcome.NonEmpty, hypothesis, prepared, fOpt, polygon, mins, maxs, threshold,
            resid, judgments, chi, tracerOrder);
    }

    private static FeasibleRegion Region(
        FeasibilityOutcome outcome,
        FractionationHypothesis hypothesis,
        PreparedInputs prepared,
        double[]? representative,
        Polygon2? polygon,
        double[]? mins,
        double[]? maxs,
        double threshold,
        double[]? residuals,
        List<Judgment> judgments,
        double? chiAt = null,
        string[]? residualTracers = null) => new()
    {
        Outcome = outcome,
        HypothesisId = hypothesis.HypothesisId,
        HypothesisLabel = hypothesis.Label,
        ProportionOrder = prepared.EndmemberOrder.ToArray(),
        RepresentativeProportions = representative,
        Polygon = polygon,
        MinProportions = mins,
        MaxProportions = maxs,
        ChiSquareThreshold = threshold,
        ChiSquareAtRepresentative = chiAt,
        Residuals = residuals,
        ResidualTracerOrder = residualTracers,
        Judgments = judgments
    };

    private static double[] Neg(double[] v) => v.Select(x => -x).ToArray();

    private static double Quad(double[] v, double[,] m) => Polytope.Inner(v, LinearAlgebra.Multiply(m, v));

    private static double[] ToProportions(double[] y)
    {
        int d = y.Length;
        var f = new double[d + 1];
        double sum = 0;
        for (int k = 0; k < d; k++) { f[k + 1] = y[k]; sum += y[k]; }
        f[0] = 1 - sum;
        return f;
    }

    private static double[] Residuals(double[] f, PreparedInputs prepared, double[,] eff, double[,] cov,
        out double chiSquare, out string[] tracerOrder)
    {
        int n = prepared.ObservedTracers.Length;
        int m = prepared.EndmemberOrder.Count;
        var pred = new double[n];
        for (int j = 0; j < n; j++)
        {
            double s = 0;
            for (int k = 0; k < m; k++) s += f[k] * eff[k, j];
            pred[j] = s;
        }
        var r = prepared.Observed.Zip(pred, (o, p) => o - p).ToArray();
        var wr = LinearAlgebra.Multiply(LinearAlgebra.InvertSPD(cov), r);
        chiSquare = Polytope.Inner(r, wr);
        tracerOrder = prepared.ObservedTracers.ToArray();
        return r;
    }

    private static EllipseRegion SafeEllipse(double[,] h, double[] g, double threshold, int d)
    {
        var (vals, vecs) = LinearAlgebra.SymmetricEigen(h);
        if (vals[0] < 1e-10)
        {
            var hreg = (double[,])h.Clone();
            for (int i = 0; i < d; i++) hreg[i, i] += 1e-8;
            var c = LinearAlgebra.SolveSPD(hreg, g);
            return new EllipseRegion { Center = c, H = hreg, Threshold = threshold };
        }
        var center = LinearAlgebra.SolveSPD(h, g);
        return new EllipseRegion { Center = center, H = h, Threshold = threshold };
    }

    private static bool PriorInfeasible(Polytope poly, int m)
        => poly.Vertices.Count == 0 && poly.Dim >= 0;

    private static (double[] mins, double[] maxs) BoundsFromPolygon(List<double[]> verts)
    {
        if (verts.Count == 0) return (Array.Empty<double>(), Array.Empty<double>());
        int d = verts[0].Length;
        var mins = new double[d + 1];
        var maxs = new double[d + 1];
        var fVerts = verts.Select(ToProportions).ToList();
        for (int k = 0; k <= d; k++)
        {
            mins[k] = fVerts.Min(f => f[k]);
            maxs[k] = fVerts.Max(f => f[k]);
        }
        return (mins, maxs);
    }

    private static (double[] mins, double[] maxs) BoundsFromEllipse(Polytope poly, EllipseRegion ellipse)
    {
        // 对每个比例方向在多面体∩椭圆上做一维扫描（d=0/1 精确；高维用投影线+采样估计）
        int d = poly.Dim;
        var mins = new double[d + 1];
        var maxs = new double[d + 1];
        var points = new List<double[]>(poly.Vertices);
        var center = poly.Project(ellipse.Center);
        points.Add(center);
        foreach (var (a, b) in EllipseRegion.PolytopeEdges(poly))
        {
            for (int t = 0; t <= 40; t++)
            {
                double u = t / 40.0;
                var p = a.Zip(b, (ai, bi) => ai + u * (bi - ai)).ToArray();
                if (ellipse.Contains(p, 1e-7)) points.Add(p);
            }
        }
        var fs = points.Where(p => ellipse.Contains(p, 1e-7)).Select(ToProportions).ToList();
        if (fs.Count == 0) fs.Add(ToProportions(center));
        for (int k = 0; k <= d; k++)
        {
            mins[k] = fs.Min(f => f[k]);
            maxs[k] = fs.Max(f => f[k]);
        }
        return (mins, maxs);
    }

    /// <summary>样品是否在有效端元（含分馏）凸包之外：用单纯形 QP 最小残差，残差卡方超阈值即外。</summary>
    private static bool OutsideConvexHull(PreparedInputs prepared, double[,] eff, double[,] cinv)
    {
        int n = prepared.ObservedTracers.Length;
        int m = prepared.EndmemberOrder.Count;
        int d = m - 1;
        double[] em0 = Enumerable.Range(0, n).Select(j => eff[0, j]).ToArray();
        var ar = new double[n, d];
        for (int k = 0; k < d; k++)
            for (int j = 0; j < n; j++)
                ar[j, k] = eff[k + 1, j] - em0[j];
        var z = prepared.Observed.Zip(em0, (o, e) => o - e).ToArray();
        var h = LinearAlgebra.Multiply(LinearAlgebra.Transpose(ar), LinearAlgebra.Multiply(cinv, ar));
        var g = LinearAlgebra.Multiply(LinearAlgebra.Transpose(ar), LinearAlgebra.Multiply(cinv, z));
        var ellipse = SafeEllipse(h, g, ChiSquare.CriticalValue(0.95, Math.Max(n, 1)), d);
        var poly = Polytope.Build(m, null);
        return !ellipse.Intersects(poly);
    }
}

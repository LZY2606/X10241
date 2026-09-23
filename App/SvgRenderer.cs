using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IsoMix.Domain;

namespace IsoMix.App;

/// <summary>纯服务端 SVG：同位素空间/端元包络/可行比例区域/残差。每个图元带稳定 data-id。</summary>
public static class SvgRenderer
{
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public sealed class View2D
    {
        public double MinX, MaxX, MinY, MaxY;
        public double W = 520, H = 420, Ml = 56, Mr = 16, Mt = 30, Mb = 48;
        public double SX(double x) => Ml + (x - MinX) / (MaxX - MinX) * (W - Ml - Mr);
        public double SY(double y) => H - Mb - (y - MinY) / (MaxY - MinY) * (H - Mt - Mb);
    }

    public static string IsotopeSpace(MixingResult r, string hypId)
    {
        var sb = new StringBuilder();
        var pts = new List<(double x, double y)>();
        int d = Math.Min(2, r.EndmemberMatrix.GetLength(0));
        for (int m = 0; m < r.EndmemberIds.Count; m++)
            pts.Add((r.EndmemberMatrix[0, m], d == 2 ? r.EndmemberMatrix[1, m] : 0));
        double y0 = d == 2 ? r.Observation[1] : 0;
        var sample = (r.Observation[0], y0);

        double repPredX = 0, repPredY = 0;
        if (r.PointEstimate is not null)
        {
            for (int m = 0; m < r.EndmemberIds.Count; m++)
            {
                repPredX += r.PointEstimate[m] * r.EndmemberMatrix[0, m];
                if (d == 2) repPredY += r.PointEstimate[m] * r.EndmemberMatrix[1, m];
            }
        }

        var allX = pts.Select(p => p.x).Concat(new[] { sample.Item1, repPredX }).ToList();
        var allY = pts.Select(p => p.y).Concat(new[] { sample.Item2, repPredY }).ToList();
        double padX = 1.6, padY = 1.6;
        var view = new View2D
        {
            MinX = Math.Floor(allX.Min() - padX), MaxX = Math.Ceiling(allX.Max() + padX),
            MinY = Math.Floor(allY.Min() - padY), MaxY = Math.Ceiling(allY.Max() + padY)
        };

        sb.Append($"<svg data-id=\"svg.isotope.{hypId}\" viewBox=\"0 0 {F(view.W)} {F(view.H)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"同位素空间\">");
        sb.Append("<rect width='100%' height='100%' fill='#ffffff'/>");
        Grid(sb, view);

        // 端元凸包（三角）= 包络
        var poly = string.Join(" ", pts.Select(p => $"{F(view.SX(p.x))},{F(view.SY(p.y))}"));
        sb.Append($"<polygon data-id=\"envelope.{hypId}\" points='{poly}' fill='rgba(70,130,210,0.10)' stroke='#2f6fb0' stroke-width='2'/>");
        for (int m = 0; m < pts.Count; m++)
        {
            sb.Append($"<circle data-id=\"endmember.{r.EndmemberIds[m]}\" cx='{F(view.SX(pts[m].x))}' cy='{F(view.SY(pts[m].y))}' r='5' fill='#2f6fb0'/>");
            sb.Append($"<text data-id=\"label.{r.EndmemberIds[m]}\" x='{F(view.SX(pts[m].x) + 8)}' y='{F(view.SY(pts[m].y) - 8)}' font-size='11' fill='#16406b'>{Esc(r.EndmemberNames[m])}</text>");
        }

        // 样品
        sb.Append($"<circle data-id=\"sample.{hypId}\" cx='{F(view.SX(sample.Item1))}' cy='{F(view.SY(sample.Item2))}' r='6' fill='#d1495b'/>");
        sb.Append($"<text data-id=\"sample-label.{hypId}\" x='{F(view.SX(sample.Item1) + 9)}' y='{F(view.SY(sample.Item2) + 4)}' font-size='11' fill='#7a1f2e'>样品</text>");

        // 残差向量：代表/点估计预测 -> 样品
        if (r.PointEstimate is not null && (Math.Abs(repPredX - sample.Item1) + Math.Abs(repPredY - sample.Item2)) > 1e-6)
        {
            sb.Append($"<line data-id=\"residual.{hypId}\" x1='{F(view.SX(repPredX))}' y1='{F(view.SY(repPredY))}' x2='{F(view.SX(sample.Item1))}' y2='{F(view.SY(sample.Item2))}' stroke='#e08a1e' stroke-width='2' stroke-dasharray='5,4' marker-end='url(#arrow-{Safe(hypId)})'/>");
        }
        sb.Append($"<defs><marker id='arrow-{Safe(hypId)}' markerWidth='8' markerHeight='8' refX='6' refY='3' orient='auto'><path d='M0,0 L6,3 L0,6 Z' fill='#e08a1e'/></marker></defs>");

        // 轴标题（带单位/基准）
        sb.Append($"<text data-id=\"axis-x.{hypId}\" x='{F(view.W / 2)}' y='{F(view.H - 8)}' text-anchor='middle' font-size='12' fill='#333'>{Esc(AxisLabel(r.Axes, 0))}</text>");
        sb.Append($"<text data-id=\"axis-y.{hypId}\" x='16' y='{F(view.H / 2)}' text-anchor='middle' font-size='12' fill='#333' transform='rotate(-90 16 {F(view.H / 2)})'>{Esc(AxisLabel(r.Axes, d == 2 ? 1 : 0))}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string FractionTriangle(MixingResult r, string hypId)
    {
        double W = 520, H = 440, M = 60;
        // simplex 顶点（a,b 平面）: A=(0,0), B=(1,0), C=(0,1)
        (double x, double y) A = (M, H - M), B = (W - M, H - M), C = (M, M);
        double tri(double a, double bb, out double x, out double y)
        { x = A.x + (B.x - A.x) * a + (C.x - A.x) * bb; y = A.y + (B.y - A.y) * a + (C.y - A.y) * bb; return 0; }

        var sb = new StringBuilder();
        sb.Append($"<svg data-id=\"svg.fractions.{hypId}\" viewBox=\"0 0 {F(W)} {F(H)}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"可行混合比例区域\">");
        sb.Append("<rect width='100%' height='100%' fill='#ffffff'/>");
        string triPts = $"{F(A.x)},{F(A.y)} {F(B.x)},{F(B.y)} {F(C.x)},{F(C.y)}";
        sb.Append($"<polygon data-id=\"simplex.{hypId}\" points='{triPts}' fill='#f4f7fb' stroke='#444' stroke-width='1.6'/>");

        // 可行网格（淡蓝），严格位于单纯形内
        if (r.GridFeasible is not null && r.GridF1 is not null && r.GridF2 is not null)
        {
            int n = r.GridF1.Length - 1;
            double cw = (B.x - A.x) / n, ch = (A.y - C.y) / n;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double a = r.GridF1[i], b = r.GridF2[j];
                    if (a + b > 1.0) continue;
                    if (r.GridFeasible[i, j])
                    {
                        tri(a, b, out double x, out double y);
                        sb.Append($"<rect data-id=\"feasible-cell.{hypId}.{i}.{j}\" x='{F(x)}' y='{F(y - ch)}' width='{F(cw + 0.7)}' height='{F(ch + 0.7)}' fill='rgba(46,139,87,0.28)' stroke='none'/>");
                    }
                }
        }

        // 等值边界环（深绿折线）
        int ci = 0;
        foreach (var comp in r.Components)
        {
            foreach (var loop in comp.BoundaryLoops)
            {
                var pp = loop.Select(p => { tri(p[0], p[1], out double x, out double y); return $"{F(x)},{F(y)}"; });
                sb.Append($"<polygon data-id=\"region-boundary.{hypId}.c{comp.Index}\" points='{string.Join(" ", pp)}' fill='none' stroke='#1f7a4d' stroke-width='2'/>");
            }
            if (comp.Representative?.Fractions is { } f)
            {
                tri(f[0], f[1], out double rx, out double ry);
                sb.Append($"<circle data-id=\"candidate.{hypId}.c{comp.Index}\" cx='{F(rx)}' cy='{F(ry)}' r='5' fill='#d1495b' stroke='#fff' stroke-width='1.5'/>");
                sb.Append($"<text data-id=\"candidate-label.{hypId}.c{comp.Index}\" x='{F(rx + 8)}' y='{F(ry - 8)}' font-size='11' fill='#7a1f2e'>候选 {comp.Index + 1}</text>");
            }
            ci++;
        }

        // 点估计（黑边白心菱形），标注“仅参考”
        if (r.PointEstimate is { } pe)
        {
            tri(pe[0], pe[1], out double px, out double py);
            sb.Append($"<polygon data-id=\"point-estimate.{hypId}\" points='{F(px)},{F(py - 6)} {F(px + 6)},{F(py)} {F(px)},{F(py + 6)} {F(px - 6)},{F(py)}' fill='#fff' stroke='#222' stroke-width='1.6'/>");
        }

        // 顶点标签（端元稳定 id）
        LabelVertex(sb, A, r.EndmemberNames, r.EndmemberIds, 2);
        LabelVertex(sb, B, r.EndmemberNames, r.EndmemberIds, 0);
        LabelVertex(sb, C, r.EndmemberNames, r.EndmemberIds, 1);

        if (!r.Identifiable)
            sb.Append($"<text data-id=\"ident-warning.{hypId}\" x='{F(W / 2)}' y='24' text-anchor='middle' font-size='12' fill='#a23b1f'>可识别性不足：展示可行域而非唯一比例</text>");
        if (r.PriorInfeasible)
            sb.Append($"<text data-id=\"prior-warning.{hypId}\" x='{F(W / 2)}' y='24' text-anchor='middle' font-size='12' fill='#a23b1f'>先验不可行：未伪造比例</text>");

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void LabelVertex(StringBuilder sb, (double x, double y) v, List<string> names, List<string> ids, int index)
    {
        if (index >= names.Count) return;
        double dx = v.x, dy = v.y;
        string anchor = "middle";
        if (v.x < 80) { dx = v.x - 6; anchor = "end"; }
        else if (v.x > 460) { dx = v.x + 6; anchor = "start"; }
        sb.Append($"<text data-id=\"vertex.{ids[index]}\" x='{F(dx)}' y='{F(v.y + 22)}' text-anchor='{anchor}' font-size='11' fill='#16406b'>{Esc(names[index])}</text>");
    }

    private static void Grid(StringBuilder sb, View2D v)
    {
        for (double gx = Math.Ceiling(v.MinX); gx <= v.MaxX; gx += 2)
            sb.Append($"<line x1='{F(v.SX(gx))}' y1='{F(v.SY(v.MinY))}' x2='{F(v.SX(gx))}' y2='{F(v.SY(v.MaxY))}' stroke='#eef1f5'/>");
        for (double gy = Math.Ceiling(v.MinY); gy <= v.MaxY; gy += 2)
            sb.Append($"<line x1='{F(v.SX(v.MinX))}' y1='{F(v.SY(gy))}' x2='{F(v.SX(v.MaxX))}' y2='{F(v.SY(gy))}' stroke='#eef1f5'/>");
    }

    private static string AxisLabel(List<string> axes, int idx)
    {
        if (idx >= axes.Count) return "—";
        return axes[idx] switch
        {
            "d13C" => "δ13C (‰ VPDB)",
            "d15N" => "δ15N (‰ AIR)",
            "d18O" => "δ18O (‰ VSMOW)",
            _ => axes[idx]
        };
    }

    private static string Safe(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
}

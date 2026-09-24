using System.Globalization;
using System.Text;
using IsotopeMixing.Core;

namespace IsotopeMixing.Web;

/// <summary>
/// 服务器端 SVG：同位素空间(样品+端元包络+协方差椭圆)、可行比例区域、残差。
/// 所有元素携带 data-id（稳定身份），便于页面复核追溯。
/// </summary>
public static class SvgCharts
{
    private static string N(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    public static string IsotopeSpace(MixingResult result,
        double[,] endmembers, string[] sourceIds, string[] sourceLabels,
        double[] sample, double[,] cov, string[] dimNames)
    {
        double minX = Math.Min(sample[0], MinCol(endmembers, 0)) - 2;
        double maxX = Math.Max(sample[0], MaxCol(endmembers, 0)) + 2;
        double minY = Math.Min(sample[1], MinCol(endmembers, 1)) - 2;
        double maxY = Math.Max(sample[1], MaxCol(endmembers, 1)) + 2;

        var sb = new StringBuilder();
        var plt = new Plot(sb, 520, 380, minX, maxX, minY, maxY, dimNames[0], dimNames[1]);
        plt.Begin();

        // 端元凸包
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i < sourceIds.Length; i++) pts.Add((endmembers[i, 0], endmembers[i, 1]));
        var hull = LinearAlgebra.ConvexHull2D(pts);
        var poly = new StringBuilder();
        foreach (int hi in hull) poly.Append($"{N(plt.X(pts[hi].X))},{N(plt.Y(pts[hi].Y))} ");
        sb.Append($"<polygon data-id=\"envelope\" points=\"{poly}\" class=\"envelope\"/>");

        // 端元点
        for (int i = 0; i < sourceIds.Length; i++)
        {
            sb.Append($"<circle data-id=\"src-{sourceIds[i]}\" cx=\"{N(plt.X(endmembers[i, 0]))}\" " +
                      $"cy=\"{N(plt.Y(endmembers[i, 1]))}\" r=\"5\" class=\"source\"/>");
            sb.Append($"<text x=\"{N(plt.X(endmembers[i, 0]) + 0.4)}\" y=\"{N(plt.Y(endmembers[i, 1]) - 0.5)}\" " +
                      $"class=\"lbl\">{System.Net.WebUtility.HtmlEncode(sourceLabels[i])}</text>");
        }

        // 95% 协方差椭圆（相关误差可视化）
        double rho = cov[0, 1] / Math.Sqrt(cov[0, 0] * cov[1, 1]);
        double angle = 0.5 * Math.Atan2(2 * cov[0, 1], cov[0, 0] - cov[1, 1]);
        double s1 = Math.Sqrt(Math.Max(0, (cov[0, 0] + cov[1, 1]) / 2.0
            + Math.Sign(cov[0, 0] - cov[1, 1] == 0 ? 1e-12 : cov[0, 0] - cov[1, 1]) *
              Math.Sqrt(((cov[0, 0] - cov[1, 1]) / 2.0) * ((cov[0, 0] - cov[1, 1]) / 2.0) + cov[0, 1] * cov[0, 1])));
        double s2 = Math.Sqrt(Math.Max(0, cov[0, 0] + cov[1, 1] - s1 * s1));
        double chi = ChiSquare.Critical95(2);
        sb.Append(Ellipse(plt, sample[0], sample[1], Math.Sqrt(chi * s1 * s1 / Math.Max(s2 * s2, 1e-12)) * Math.Sqrt(Math.Max(s2*s2,1e-12)),
            Math.Sqrt(chi) * Math.Sqrt(Math.Max(s2*s2,1e-12)), angle, $"cov-{result.SampleId}"));

        sb.Append($"<circle data-id=\"sample-{result.SampleId}\" cx=\"{N(plt.X(sample[0]))}\" " +
                  $"cy=\"{N(plt.Y(sample[1]))}\" r=\"5\" class=\"sample\"/>");
        sb.Append($"<text x=\"{N(plt.X(sample[0]) + 0.4)}\" y=\"{N(plt.Y(sample[1]) + 1.4)}\" " +
                  $"class=\"lbl sample-lbl\">{System.Net.WebUtility.HtmlEncode(result.SampleLabel ?? result.SampleId)}</text>");

        plt.End();
        return sb.ToString();
    }

    public static string FractionSpace(MixingResult result, string[] sourceIds, string[] sourceLabels)
    {
        var accepted = result.Candidates.Where(c => c.Accepted).ToList();
        var considered = result.Candidates;
        double min = -0.05, max = 1.05;
        var sb = new StringBuilder();
        var plt = new Plot(sb, 520, 380, min, max, min, max, "投影轴 u1", "投影轴 u2");
        plt.Begin();

        var projected = new List<(double X, double Y, FeasibleVertex V)>();
        foreach (var v in result.Vertices)
        {
            var (x, y) = ProjectVertex(v.Fractions, sourceIds);
            projected.Add((x, y, v));
        }

        if (projected.Count >= 3)
        {
            var hull = LinearAlgebra.ConvexHull2D(projected.Select(p => (p.X, p.Y)).ToList());
            var poly = new StringBuilder();
            foreach (int hi in hull)
                poly.Append($"{N(plt.X(projected[hi].X))},{N(plt.Y(projected[hi].Y))} ");
            sb.Append($"<polygon data-id=\"simplex-hull\" points=\"{poly}\" class=\"simplex\"/>");
        }
        foreach (var p in projected)
        {
            string cls = p.V.Accepted ? "v-accepted" : "v-rejected";
            sb.Append($"<circle data-id=\"vertex-{string.Join("-", p.V.SourceIds)}\" " +
                      $"cx=\"{N(plt.X(p.X))}\" cy=\"{N(plt.Y(p.Y))}\" r=\"4\" class=\"{cls}\"/>");
        }
        foreach (var c in considered)
        {
            var (x, y) = ProjectVertex(PadFractions(c, sourceIds), sourceIds);
            string cls = c.Accepted ? "candidate-ok" : "candidate-bad";
            sb.Append($"<circle data-id=\"{c.CandidateId}\" cx=\"{N(plt.X(x))}\" cy=\"{N(plt.Y(y))}\" " +
                      $"r=\"7\" class=\"{cls}\"/>");
            sb.Append($"<text x=\"{N(plt.X(x) + 0.02)}\" y=\"{N(plt.Y(y) - 0.03)}\" class=\"lbl\">" +
                      $"χ²={N(c.Chi2)}</text>");
        }
        plt.End();
        return sb.ToString();
    }

    public static string Residuals(MixingResult result, string[] dimLabels, string[] usedDims)
    {
        var cand = result.Candidates.Where(c => c.Accepted).ToList();
        var shown = cand.Count > 0 ? cand.Take(6).ToList() : result.Candidates.Take(6).ToList();
        if (shown.Count == 0)
        {
            return "<svg class=\"chart\" viewBox=\"0 0 520 380\"><text x=\"20\" y=\"40\" class=\"lbl\">无候选可显示</text></svg>";
        }
        int dd = shown[0].Residual.Length;
        double maxAbs = shown.SelectMany(c => c.Residual).Select(Math.Abs).DefaultIfEmpty(1).Max() * 1.2;
        var sb = new StringBuilder();
        var plt = new Plot(sb, 520, 380, -0.5, dd - 0.5, -maxAbs, maxAbs, "同位素维", "残差 y-Ef-Δ (‰)");
        plt.Begin();
        sb.Append($"<line x1=\"{N(plt.X(-0.5))}\" y1=\"{N(plt.Y(0))}\" x2=\"{N(plt.X(dd - 0.5))}\" " +
                  $"y2=\"{N(plt.Y(0))}\" class=\"axis\"/>");
        int ci = 0;
        foreach (var c in shown)
        {
            string colorClass = c.Accepted ? "res-ok" : "res-bad";
            for (int d = 0; d < dd; d++)
            {
                double x = d + (ci - (shown.Count - 1) / 2.0) * 0.12;
                sb.Append($"<line data-id=\"res-{c.CandidateId}-{usedDims[d]}\" " +
                          $"x1=\"{N(plt.X(x))}\" y1=\"{N(plt.Y(0))}\" x2=\"{N(plt.X(x))}\" " +
                          $"y2=\"{N(plt.Y(c.Residual[d]))}\" class=\"{colorClass}\" stroke-width=\"4\"/>");
            }
            ci++;
        }
        for (int d = 0; d < dd; d++)
            sb.Append($"<text x=\"{N(plt.X(d))}\" y=\"{N(plt.Y(-maxAbs) + 16)}\" class=\"lbl axis-label\">" +
                      $"{System.Net.WebUtility.HtmlEncode(usedDims[d])}</text>");
        plt.End();
        return sb.ToString();
    }

    private static IReadOnlyList<double> PadFractions(CandidateMix c, string[] allSources)
    {
        var map = c.SourceIds.Zip(c.Fractions).ToDictionary(x => x.First, x => x.Second);
        return allSources.Select(s => map.TryGetValue(s, out var f) ? f : 0).ToList();
    }

    private static (double X, double Y) ProjectVertex(IReadOnlyList<double> f, string[] sourceIds)
    {
        int k = f.Count;
        if (k == 3)
        {
            // 三元图平面投影：保留重心坐标几何
            return (f[0] + 0.5 * f[1], Math.Sqrt(3) / 2 * f[1]);
        }
        // PCA：以单纯形顶点为参考的前两个主方向
        double mean = f.Average();
        double[] c = f.Select(v => v - mean).ToArray();
        double u = c[0];
        double w = c.Length > 1 ? c[1] : 0;
        double norm = Math.Sqrt(c.Sum(v => v * v));
        if (norm < 1e-12) return (0.5, 0.5);
        return ((u + 0.3) , (w + 0.3));
    }

    private static string Ellipse(Plot plt, double cx, double cy, double rx, double ry,
        double angle, string id)
    {
        var pts = new StringBuilder();
        for (int i = 0; i <= 40; i++)
        {
            double t = 2 * Math.PI * i / 40.0;
            double ex = rx * Math.Cos(t), ey = ry * Math.Sin(t);
            double x = cx + Math.Cos(angle) * ex - Math.Sin(angle) * ey;
            double y = cy + Math.Sin(angle) * ex + Math.Cos(angle) * ey;
            pts.Append($"{N(plt.X(x))},{N(plt.Y(y))} ");
        }
        return $"<polygon data-id=\"{id}\" points=\"{pts}\" class=\"covellipse\"/>";
    }

    private static double MinCol(double[,] m, int col)
    {
        double v = double.MaxValue;
        for (int i = 0; i < m.GetLength(0); i++) v = Math.Min(v, m[i, col]);
        return v;
    }
    private static double MaxCol(double[,] m, int col)
    {
        double v = double.MinValue;
        for (int i = 0; i < m.GetLength(0); i++) v = Math.Max(v, m[i, col]);
        return v;
    }

    private sealed class Plot
    {
        private readonly StringBuilder _sb;
        private readonly double _w, _h, _ml, _mr, _mt, _mb;
        private readonly double _xmin, _xmax, _ymin, _ymax;
        public Plot(StringBuilder sb, double w, double h,
            double xmin, double xmax, double ymin, double ymax, string xlabel, string ylabel)
        {
            _sb = sb; _w = w; _h = h;
            _ml = 52; _mr = 16; _mt = 18; _mb = 44;
            _xmin = xmin; _xmax = xmax; _ymin = ymin; _ymax = ymax;
            XLabel = xlabel; YLabel = ylabel;
        }
        public string XLabel { get; }
        public string YLabel { get; }
        public double X(double v) => _ml + (v - _xmin) / (_xmax - _xmin) * (_w - _ml - _mr);
        public double Y(double v) => _h - _mb - (v - _ymin) / (_ymax - _ymin) * (_h - _mt - _mb);

        public void Begin()
        {
            _sb.Append($"<svg class=\"chart\" data-chart viewBox=\"0 0 {_w} {_h}\" " +
                       "xmlns=\"http://www.w3.org/2000/svg\">");
            _sb.Append($"<rect x=\"{_ml}\" y=\"{_mt}\" width=\"{_w - _ml - _mr}\" " +
                       $"height=\"{_h - _mt - _mb}\" class=\"plotbg\"/>");
            _sb.Append($"<line x1=\"{_ml}\" y1=\"{_h - _mb}\" x2=\"{_w - _mr}\" y2=\"{_h - _mb}\" class=\"axis\"/>");
            _sb.Append($"<line x1=\"{_ml}\" y1=\"{_mt}\" x2=\"{_ml}\" y2=\"{_h - _mb}\" class=\"axis\"/>");
        }

        public void End()
        {
            _sb.Append($"<text x=\"{(_w) / 2}\" y=\"{_h - 8}\" class=\"axis-label\">" +
                       $"{System.Net.WebUtility.HtmlEncode(XLabel)}</text>");
            _sb.Append($"<text transform=\"translate(14,{_h / 2}) rotate(-90)\" class=\"axis-label\">" +
                       $"{System.Net.WebUtility.HtmlEncode(YLabel)}</text>");
            _sb.Append("</svg>");
        }
    }
}

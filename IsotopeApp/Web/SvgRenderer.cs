using System.Globalization;
using System.Text;

namespace IsotopeApp.Web;

/// <summary>
/// Server-rendered SVG. Every graphical element carries a stable id derived
/// from object identity (source id / candidate id / component index), so the
/// same archive re-imported elsewhere draws identical ids.
/// </summary>
public static class SvgRenderer
{
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public sealed record PlotPoint(double X, double Y, string Label, string Id);
    public sealed record Ellipse(double CX, double CY, double Rx, double Ry, double AngleDeg, string Id, string CssClass);

    public static string IsotopeSpace(
        IReadOnlyList<PlotPoint> sources,
        PlotPoint sample,
        IReadOnlyList<Ellipse> sourceEllipses,
        IReadOnlyList<PlotPoint> fitted,
        string xLabel, string yLabel,
        int w = 460, int h = 360, int pad = 56)
    {
        var allX = sources.Select(s => s.X).Concat(new[] { sample.X }).Concat(fitted.Select(f => f.X)).ToList();
        var allY = sources.Select(s => s.Y).Concat(new[] { sample.Y }).Concat(fitted.Select(f => f.Y)).ToList();
        // pad range for ellipses
        double minX = allX.Min() - 4, maxX = allX.Max() + 4;
        double minY = allY.Min() - 4, maxY = allY.Max() + 4;
        double SX(double x) => pad + (x - minX) / (maxX - minX) * (w - 2 * pad);
        double SY(double y) => h - pad - (y - minY) / (maxY - minY) * (h - 2 * pad);
        var sb = new StringBuilder();
        sb.Append($"<svg id=\"svg-isotope-space\" data-stable-id=\"svg-isotope-space\" viewBox=\"0 0 {w} {h}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"isotope space\">");
        sb.Append(Grid(w, h, pad));
        // envelope polygon
        if (sources.Count >= 3)
        {
            sb.Append("<polygon id=\"endmember-envelope\" data-stable-id=\"endmember-envelope\" points=\"");
            foreach (var s in sources) sb.Append(F(SX(s.X)) + "," + F(SY(s.Y)) + " ");
            sb.Append("\" fill=\"rgba(90,140,200,0.10)\" stroke=\"#5a8cc8\" stroke-width=\"1.5\"/>");
        }
        foreach (var e in sourceEllipses)
            sb.Append($"<g id=\"ellipse-{e.Id}\" data-stable-id=\"ellipse-{e.Id}\" transform=\"translate({F(SX(e.CX))},{F(SY(e.CY))}) rotate({F(e.AngleDeg)})\">" +
                      $"<ellipse rx=\"{F(Math.Abs(SX(e.Rx) - SX(0)))}\" ry=\"{F(Math.Abs(SY(e.Ry) - SY(0)))}\" class=\"{e.CssClass}\" fill=\"rgba(90,140,200,0.12)\" stroke=\"#5a8cc8\"/></g>");
        foreach (var s in sources)
            sb.Append($"<circle id=\"src-pt-{s.Id}\" data-stable-id=\"src-pt-{s.Id}\" cx=\"{F(SX(s.X))}\" cy=\"{F(SY(s.Y))}\" r=\"5\" class=\"source-point\"/>" +
                      $"<text x=\"{F(SX(s.X) + 8)}\" y=\"{F(SY(s.Y) - 8)}\" class=\"label\">{Esc(s.Label)}</text>");
        int fi = 0;
        foreach (var f in fitted)
        {
            sb.Append($"<rect id=\"fit-{f.Id}\" data-stable-id=\"fit-{f.Id}\" x=\"{F(SX(f.X) - 5)}\" y=\"{F(SY(f.Y) - 5)}\" width=\"10\" height=\"10\" class=\"fitted-point\"/>");
            fi++;
        }
        sb.Append($"<line id=\"sample-vector\" data-stable-id=\"sample-vector\" x1=\"{F(SX(sample.X))}\" y1=\"{F(SY(sample.Y))}\" x2=\"{F(SX(sample.X))}\" y2=\"{F(SY(sample.Y))}\" stroke=\"none\"/>");
        sb.Append($"<circle id=\"sample-pt-{sample.Id}\" data-stable-id=\"sample-pt-{sample.Id}\" cx=\"{F(SX(sample.X))}\" cy=\"{F(SY(sample.Y))}\" r=\"6\" class=\"sample-point\"/>" +
                  $"<text x=\"{F(SX(sample.X) + 9)}\" y=\"{F(SY(sample.Y) + 4)}\" class=\"label sample-label\">{Esc(sample.Label)}</text>");
        sb.Append(Axes(w, h, pad, minX, maxX, minY, maxY, xLabel, yLabel));
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Feasible region in proportion space. n=2 -> 1-D bar; n=3 -> ternary
    /// triangle; n>=4 -> horizontal interval bars per source.
    /// </summary>
    public static string FeasibleRegions(
        int n, IReadOnlyList<string> sourceNames,
        IReadOnlyList<(double[] Center, double[] Min, double[] Max, int Index, int Cells)> regions,
        int w = 460, int h = 360, int pad = 56)
    {
        var sb = new StringBuilder();
        if (n == 3)
        {
            double Xc(double p1, double p2) => pad + (p1 - p2 / 2.0 + 0.5) * (w - 2 * pad);
            double Yc(double p2) => h - pad - p2 * (h - 2 * pad);
            sb.Append($"<svg id=\"svg-feasible-regions\" data-stable-id=\"svg-feasible-regions\" viewBox=\"0 0 {w} {h}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"feasible proportion regions\">");
            sb.Append($"<polygon id=\"simplex-frame\" data-stable-id=\"simplex-frame\" points=\"{F(Xc(0,0))},{F(Yc(0))} {F(Xc(1,0))},{F(Yc(0))} {F(Xc(0,1))},{F(Yc(1))}\" fill=\"rgba(60,170,110,0.05)\" stroke=\"#3ca86e\"/>");
            var colors = new[] { "#1f77b4", "#d62728", "#9467bd", "#ff7f0e" };
            foreach (var r in regions)
            {
                string cls = $"feasible-region region-{r.Index}";
                // approximate region as scaled box mapped into ternary coords
                double c0 = r.Center[0], c1 = r.Center[1];
                double hw = Math.Max(0.012, (r.Max[0] - r.Min[0]) / 2);
                double hh = Math.Max(0.012, (r.Max[1] - r.Min[1]) / 2);
                var pts = new (double, double)[]
                {
                    (c0-hw,c1-hh),(c0+hw,c1-hh),(c0+hw,c1+hh),(c0-hw,c1+hh)
                }.Select(pt => $"{F(Xc(pt.Item1, pt.Item2))},{F(Yc(pt.Item2))}");
                string color = colors[r.Index % colors.Length];
                sb.Append($"<polygon id=\"feasible-region-{r.Index}\" data-stable-id=\"feasible-region-{r.Index}\" points=\"{string.Join(" ", pts)}\" fill=\"{color}\" fill-opacity=\"0.55\" stroke=\"{color}\"/>");
                sb.Append($"<circle id=\"region-center-{r.Index}\" data-stable-id=\"region-center-{r.Index}\" cx=\"{F(Xc(c0,c1))}\" cy=\"{F(Yc(c1))}\" r=\"3\" fill=\"{color}\"/>");
                sb.Append($"<text id=\"region-label-{r.Index}\" data-stable-id=\"region-label-{r.Index}\" x=\"{F(Xc(c0,c1) + 6)}\" y=\"{F(Yc(c1))}\" class=\"label\">区{r.Index + 1} (n={r.Cells})</text>");
            }
            sb.Append($"<text x=\"{F(Xc(0,-0.06))}\" y=\"{F(Yc(-0.04))}\" class=\"axis-label\">{Esc(sourceNames[0])}</text>");
            sb.Append($"<text x=\"{F(Xc(1,-0.06))}\" y=\"{F(Yc(-0.04))}\" class=\"axis-label\">{Esc(sourceNames[1])}</text>");
            sb.Append($"<text x=\"{F(Xc(0,1) - 10)}\" y=\"{F(Yc(1) - 8)}\" class=\"axis-label\">{Esc(sourceNames[2])}</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }
        // n==2: horizontal bar; n>=4 interval bars
        sb.Append($"<svg id=\"svg-feasible-regions\" data-stable-id=\"svg-feasible-regions\" viewBox=\"0 0 {w} {h}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"feasible proportion regions\">");
        if (n == 2 && regions.Count > 0)
        {
            int y = h / 2;
            sb.Append($"<line x1=\"{pad}\" y1=\"{y}\" x2=\"{w - pad}\" y2=\"{y}\" stroke=\"#ccc\"/>");
            var colors = new[] { "#1f77b4", "#d62728", "#9467bd", "#ff7f0e" };
            foreach (var r in regions)
            {
                double x0 = pad + r.Min[0] * (w - 2 * pad);
                double x1 = pad + r.Max[0] * (w - 2 * pad);
                string color = colors[r.Index % colors.Length];
                sb.Append($"<line id=\"feasible-region-{r.Index}\" data-stable-id=\"feasible-region-{r.Index}\" x1=\"{F(x0)}\" y1=\"{y}\" x2=\"{F(x1)}\" y2=\"{y}\" stroke=\"{color}\" stroke-width=\"10\" stroke-linecap=\"round\"/>");
            }
            sb.Append($"<text x=\"{pad}\" y=\"{y - 14}\" class=\"axis-label\">{Esc(sourceNames[0])} p=0</text>");
            sb.Append($"<text x=\"{w - pad - 40}\" y=\"{y - 14}\" class=\"axis-label\">p=1</text>");
        }
        else
        {
            int rowH = (h - 2 * pad) / Math.Max(1, sourceNames.Count);
            var colors = new[] { "#1f77b4", "#d62728", "#9467bd", "#ff7f0e", "#2ca02c" };
            foreach (var r in regions.Take(1))
                for (int i = 0; i < n; i++)
                {
                    int y = pad + i * rowH + rowH / 2;
                    double x0 = pad + r.Min[i] * (w - 2 * pad);
                    double x1 = pad + r.Max[i] * (w - 2 * pad);
                    sb.Append($"<text x=\"{pad - 50}\" y=\"{y + 4}\" class=\"axis-label\">{Esc(sourceNames[i])}</text>");
                    sb.Append($"<line x1=\"{pad}\" y1=\"{y}\" x2=\"{w - pad}\" y2=\"{y}\" stroke=\"#eee\"/>");
                    sb.Append($"<line id=\"feasible-interval-{r.Index}-{i}\" data-stable-id=\"feasible-interval-{r.Index}-{i}\" x1=\"{F(x0)}\" y1=\"{y}\" x2=\"{F(x1)}\" y2=\"{y}\" stroke=\"{colors[i % colors.Length]}\" stroke-width=\"8\"/>");
                }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Grid(int w, int h, int pad) =>
        $"<rect x=\"{pad}\" y=\"{pad}\" width=\"{w - 2 * pad}\" height=\"{h - 2 * pad}\" fill=\"#fff\" stroke=\"#999\"/>";

    private static string Axes(int w, int h, int pad, double minX, double maxX, double minY, double maxY, string xLabel, string yLabel)
    {
        var sb = new StringBuilder();
        for (int t = 0; t <= 4; t++)
        {
            double xv = minX + (maxX - minX) * t / 4;
            double yv = minY + (maxY - minY) * t / 4;
            double xp = pad + (w - 2 * pad) * t / 4;
            double yp = h - pad - (h - 2 * pad) * t / 4;
            sb.Append($"<text x=\"{F(xp - 14)}\" y=\"{h - pad + 18}\" class=\"tick\">{F(xv)}</text>");
            sb.Append($"<text x=\"{pad - 44}\" y=\"{F(yp + 4)}\" class=\"tick\">{F(yv)}</text>");
        }
        sb.Append($"<text x=\"{(w / 2 - 30)}\" y=\"{h - 12}\" class=\"axis-label\">{Esc(xLabel)}</text>");
        sb.Append($"<text transform=\"translate(16,{h / 2 + 30}) rotate(-90)\" class=\"axis-label\">{Esc(yLabel)}</text>");
        return sb.ToString();
    }

    private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s);
}

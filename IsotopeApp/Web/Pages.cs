using System.Globalization;
using System.Text;

namespace IsotopeApp.Web;

/// <summary>Server-rendered HTML (no required JS); every value shows its stable id.</summary>
public static class Pages
{
    public static string Esc(object? s) => System.Net.WebUtility.HtmlEncode(s?.ToString() ?? "");
    public static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public const string Style = """
    body{font-family:system-ui,-apple-system,'Segoe UI',sans-serif;margin:0;background:#f5f7fa;color:#1f2933}
    header{background:#10324f;color:#fff;padding:14px 24px;display:flex;gap:18px;align-items:center;flex-wrap:wrap}
    header a{color:#bfe0ff;text-decoration:none} header a:hover{text-decoration:underline}
    main{padding:22px;max-width:1280px;margin:0 auto}
    h1{font-size:20px;margin:0 0 6px} h2{font-size:16px;margin:22px 0 8px} h3{font-size:14px;margin:14px 0 6px}
    .card{background:#fff;border:1px solid #dbe2ea;border-radius:10px;padding:16px;margin:12px 0;box-shadow:0 1px 2px rgba(0,0,0,.04)}
    .grid2{display:grid;grid-template-columns:1fr 1fr;gap:14px}
    .grid3{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}
    table{border-collapse:collapse;width:100%;font-size:13px}
    th,td{border:1px solid #e1e7ef;padding:6px 8px;text-align:left;vertical-align:top}
    th{background:#eef3f8}
    .sid{font-family:ui-monospace,Menlo,monospace;font-size:11px;color:#5a6b7d;display:block}
    .badge{display:inline-block;padding:2px 8px;border-radius:10px;font-size:12px;margin-right:6px}
    .ok{background:#dff3e6;color:#176c3a}.warn{background:#fdeccb;color:#8a5a00}.bad{background:#fbdcdc;color:#8c2424}
    .info{background:#dcebfa;color:#144a7d}
    .diag{font-family:ui-monospace,Menlo,monospace;font-size:12px;background:#f8fafc;border-left:3px solid #5a8cc8;padding:6px 10px;margin:4px 0}
    .btn{display:inline-block;border:0;border-radius:6px;padding:7px 13px;font-size:13px;cursor:pointer;background:#1769aa;color:#fff;text-decoration:none}
    .btn.secondary{background:#5a6b7d}.btn.danger{background:#b3352e}.btn.ghost{background:#e7edf3;color:#1f2933}
    form{display:inline} .row{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin:8px 0}
    input,select,textarea{font:inherit;padding:5px 8px;border:1px solid #b9c4d0;border-radius:6px}
    .event{border-left:3px solid #5a8cc8;padding:6px 10px;margin:6px 0;background:#fbfdff}
    .event.conflict{border-left-color:#b3352e;background:#fff6f6}
    .event.decision{border-left-color:#3ca86e}
    .event.parameter{border-left-color:#b07c20}
    svg{max-width:100%;height:auto;background:#fff;border:1px solid #e1e7ef;border-radius:8px}
    .muted{color:#697787;font-size:12px}
    .raw{font-family:ui-monospace,Menlo,monospace;background:#f4f6f9;padding:4px 6px;border-radius:4px;font-size:12px}
    .sidebyside{display:grid;grid-template-columns:1fr 1fr;gap:14px}
    .residual-pos{color:#176c3a}.residual-neg{color:#b3352e}
    """;

    public static string Layout(string title, string body, string? flash = null)
    {
        return $"""<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"/>
        <meta name="viewport" content="width=device-width,initial-scale=1"/>
        <title>{Esc(title)} · 源组成室</title><style>{Style}</style></head><body>
        <header><strong>源组成室</strong>
        <a href="/">仪表盘</a><a href="/cases">案例包</a><a href="/branches">分支</a>
        <a href="/archive/export">导出档案</a>
        <form method="post" action="/archive/reload" style="margin-left:auto">
          <button class="btn ghost" type="submit">重置并载入仓库示例</button>
        </form></header><main>
        <h1>{Esc(title)}</h1>
        {(flash is null ? "" : $"<div class='card'>{flash}</div>")}
        {body}</main></body></html>""";
    }
}

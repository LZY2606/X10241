using System.Globalization;
using System.Text.RegularExpressions;

namespace IsotopeMix.Domain;

/// <summary>
/// 一个领域量的“原始表示 + 规范化表示”。原始表示永不丢失；无法规范化时
/// 保留在原位置并附诊断（Status != ok），派生层不得静默丢弃。
/// </summary>
public sealed class RawQuantity
{
    public string Dimension { get; set; } = "";
    public string Raw { get; set; } = "";
    public double? Value { get; set; }
    public string? Unit { get; set; }
    public string? OriginalUnit { get; set; }
    public string? Datum { get; set; }
    public string? OriginalDatum { get; set; }
    public string Status { get; set; } = "ok"; // ok | missing | unparseable | unconvertible
    public string? Diagnostic { get; set; }
    public string ValueKind { get; set; } = "delta"; // delta | fraction | coordinate

    public bool Usable => Status == "ok" && Value.HasValue;
}

public static class QuantityParser
{
    private static readonly string[] MissingTokens =
        { "", "na", "n/a", "null", "none", "—", "-", "–", "missing", "未测", "缺失" };

    // d18O VPDB -> VSMOW (Coplen 1988): VSMOW = 1.030928*VPDB + 30.92
    private const double O18Scale = 1.030928;
    private const double O18Offset = 30.92;

    public static RawQuantity ParseDelta(string dimension, string? raw)
    {
        var q = new RawQuantity { Dimension = dimension, ValueKind = "delta", Raw = raw ?? "" };
        var text = (raw ?? "").Trim();
        if (MissingTokens.Contains(text.ToLowerInvariant()))
        {
            q.Status = "missing";
            q.Diagnostic = "原始记录缺失该同位素维；保留空位，不从其他维推断。";
            return q;
        }

        var numMatch = Regex.Match(text, @"-?\d+(?:\.\d+)?");
        if (!numMatch.Success)
        {
            q.Status = "unparseable";
            q.Diagnostic = $"无法从原始文本“{text}”读取数值；原值保留在原位置。";
            return q;
        }
        if (!double.TryParse(numMatch.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            q.Status = "unparseable";
            q.Diagnostic = $"数值“{numMatch.Value}”无法解析。";
            return q;
        }

        var unit = CanonicalUnit(text);
        if (unit is null)
        {
            q.Status = "unconvertible";
            q.OriginalUnit = ExtractUnitToken(text, numMatch);
            q.Diagnostic = "未识别或与该同位素量不相称的单位，无法规范化；原始表示保留。";
            return q;
        }
        q.OriginalUnit = unit;
        q.Unit = "permille";

        var datum = ExtractDatum(text);
        q.OriginalDatum = datum;
        if (datum is null)
        {
            q.Status = "unconvertible";
            q.Diagnostic = "缺少或无法识别同位素基准（如 VPDB/VSMOW），不予臆测；原始表示保留。";
            return q;
        }

        var permille = unit == "percent" ? number * 10.0 : number;

        switch (dimension)
        {
            case "d18O":
                if (datum == "VSMOW") { q.Value = (permille - O18Offset) / O18Scale; q.Datum = "VPDB"; }
                else if (datum == "VPDB") { q.Value = permille; q.Datum = "VPDB"; }
                else { q.Status = "unconvertible"; q.Diagnostic = $"d18O 基准 {datum} 无转换规则。"; return q; }
                break;
            case "d13C":
                if (datum != "VPDB")
                {
                    q.Status = "unconvertible";
                    q.Diagnostic = $"d13C 仅接受 VPDB 记录；收到 {datum}，无可靠换算，原值保留。";
                    return q;
                }
                q.Value = permille; q.Datum = "VPDB";
                break;
            case "d15N":
                if (datum != "AIR")
                {
                    q.Status = "unconvertible";
                    q.Diagnostic = $"d15N 仅接受 AIR 基准；收到 {datum}，原值保留。";
                    return q;
                }
                q.Value = permille; q.Datum = "AIR";
                break;
            default:
                q.Status = "unconvertible";
                q.Diagnostic = $"未知同位素维 {dimension}，不做规范化。";
                return q;
        }
        return q;
    }

    private static string? CanonicalUnit(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains('‰') || t.Contains("‰") || t.Contains("per-mil") || t.Contains("permille")
            || t.Contains("permil") || t.Contains("o/oo") || t.Contains("per mille") || t.Contains("千分"))
            return "permille";
        if (t.Contains('%') || t.Contains("percent") || t.Contains("pct") || t.Contains("百分"))
            return "percent";
        if (t.Contains("ppm") || t.Contains("ppb") || t.Contains("mg/l"))
            return null;
        return null;
    }

    private static string ExtractUnitToken(string text, Match numMatch)
    {
        var tail = text.Substring(numMatch.Index + numMatch.Length).Trim();
        return string.IsNullOrWhiteSpace(tail) ? "?" : tail;
    }

    private static string? ExtractDatum(string text)
    {
        var t = text.ToUpperInvariant();
        if (t.Contains("VPDB") || t.Contains("PDB")) return "VPDB";
        if (t.Contains("VSMOW") || t.Contains("SMOW")) return "VSMOW";
        if (t.Contains("AIR")) return "AIR";
        var m = Regex.Match(text, @"(?i)\b(VPDX|[A-Z]{2,6})\b");
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}

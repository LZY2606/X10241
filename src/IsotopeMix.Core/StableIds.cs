using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsotopeMix.Core;

/// <summary>
/// 内容寻址身份：同内容永远得到同一身份；一个数值被重新解释/修订后会得到新的记录身份，
/// 但 logical_key（材料身份）保持不变。身份在导出/删除/导入后保持稳定。
/// </summary>
public static class StableIds
{
    public static string Sha256Hex(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>记录身份：rec_&lt;kind&gt;_&lt;内容哈希前16位&gt;。</summary>
    public static string RecordId(string kind, string canonicalContent)
        => $"rec_{kind}_{Sha256Hex(canonicalContent)[..16]}";

    /// <summary>事件身份由分支、序号、类型与载荷内容确定，跨库重放保持一致。</summary>
    public static string EventId(string branchId, long seq, string kind, string canonicalPayload)
        => $"evt_{Sha256Hex($"{branchId}|{seq}|{kind}|{canonicalPayload}")[..16]}";

    /// <summary>
    /// 规范 JSON：递归按键名排序、无空白。用于哈希，使身份不受属性书写顺序影响。
    /// </summary>
    public static string Canonical(JsonElement element)
    {
        var sb = new StringBuilder();
        WriteCanonical(element, sb);
        return sb.ToString();
    }

    public static string Canonical(object value, JsonSerializerOptions options)
        => Canonical(JsonSerializer.SerializeToElement(value, options));

    private static void WriteCanonical(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject()
                             .OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(Escape(property.Name)).Append("\":");
                    WriteCanonical(property.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    WriteCanonical(item, sb);
                }
                sb.Append(']');
                break;
            case JsonValueKind.String:
                sb.Append('"').Append(Escape(element.GetString() ?? string.Empty)).Append('"');
                break;
            case JsonValueKind.Number:
                sb.Append(element.GetRawText());
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            default:
                sb.Append("null");
                break;
        }
    }

    private static string Escape(string s) => JsonSerializer.Serialize(s)[1..^1];
}

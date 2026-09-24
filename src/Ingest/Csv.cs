using System.Text;

namespace IsotopeMixing.Ingest;

/// <summary>最小 RFC4180 风格 CSV 读取器（带引号、逗号、空单元格=缺失）。</summary>
public static class Csv
{
    public static List<Dictionary<string, string>> Parse(byte[] content)
    {
        string text = new(Encoding.UTF8.GetString(content).Where(c => c != '\r').ToArray());
        var rows = new List<List<string>>();
        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new(); }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }

        if (rows.Count == 0) return new();
        var header = rows[0].Select(h => h.Trim()).ToList();
        var result = new List<Dictionary<string, string>>();
        foreach (var rawRow in rows.Skip(1))
        {
            if (rawRow.Count == 1 && string.IsNullOrWhiteSpace(rawRow[0])) continue;
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < header.Count; i++)
                dict[header[i]] = i < rawRow.Count ? rawRow[i].Trim() : "";
            result.Add(dict);
        }
        return result;
    }
}

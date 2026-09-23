using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsotopeMixing.Cases;

public static class CaseLoader
{
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static (CaseFile file, JsonNode manifestNode, string rawText) LoadFromDirectory(string caseDir)
    {
        var path = Path.Combine(caseDir, "case.json");
        var text = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<CaseFile>(text, ReadOptions)
            ?? throw new InvalidDataException("case.json deserialized to null");
        var node = JsonNode.Parse(text)!;
        return (file, node, text);
    }

    public static string? FindCaseDirectory(string? explicitPath = null)
    {
        var candidates = new List<string>();
        if (explicitPath is not null) candidates.Add(explicitPath);
        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "cases", "source-composition-chamber"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "cases", "source-composition-chamber"));
        var up = cwd;
        for (int i = 0; i < 6 && up is not null; i++)
        {
            candidates.Add(Path.Combine(up, "cases", "source-composition-chamber"));
            up = Path.GetDirectoryName(up);
        }
        return candidates.FirstOrDefault(Directory.Exists);
    }
}

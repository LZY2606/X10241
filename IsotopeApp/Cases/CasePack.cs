using System.Text.Json;

namespace IsotopeApp.Cases;

/// <summary>
/// Strongly-typed view over one file of the domain offline case pack. The raw
/// JSON element is kept alongside parsed fields so import can persist the
/// original representation untouched.
/// </summary>
public sealed class CasePack
{
    public required string CaseId { get; init; }
    public required string Title { get; init; }
    public required string Stage { get; init; }
    public required JsonDocument Doc { get; init; }
    public required string FilePath { get; init; }
    public string RawText { get; init; } = "";
}

public sealed class CaseLibrary
{
    private readonly string _root;
    public CaseLibrary(string root) { _root = root; }

    public static string DefaultRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Cases"),
            Path.Combine(Directory.GetCurrentDirectory(), "Cases"),
            Path.Combine(Directory.GetCurrentDirectory(), "cases"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "cases"))
        };
        foreach (var c in candidates) if (Directory.Exists(c)) return c;
        return candidates[0];
    }

    public IReadOnlyList<CasePack> LoadAll()
    {
        var list = new List<CasePack>();
        foreach (var file in Directory.EnumerateFiles(_root, "case-*.json").OrderBy(f => f))
        {
            string text = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            list.Add(new CasePack
            {
                CaseId = root.GetProperty("caseId").GetString()!,
                Title = root.GetProperty("title").GetString()!,
                Stage = root.GetProperty("narrativeStage").GetString()!,
                Doc = JsonDocument.Parse(text),
                FilePath = file,
                RawText = text
            });
        }
        return list;
    }

    public CasePack Load(string caseId)
        => LoadAll().First(c => c.CaseId == caseId);
}

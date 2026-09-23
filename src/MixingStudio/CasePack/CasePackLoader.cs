using System.Text.Json;
using System.Text.Json.Serialization;
using MixingStudio.Engine;

namespace MixingStudio.CasePack;

public sealed class CasePackMaterial
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
}

public sealed class CounterexampleCase
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("changeSource")] public string ChangeSource { get; set; } = "";
    [JsonPropertyName("narrative")] public string Narrative { get; set; } = "";
    [JsonPropertyName("materials")] public List<CasePackMaterial> Materials { get; set; } = new();
    [JsonPropertyName("expect")] public JsonElement Expect { get; set; }
}

public sealed class CasePackManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("baseline")] public List<CasePackMaterial> Baseline { get; set; } = new();
    [JsonPropertyName("counterexamples")] public List<CounterexampleCase> Counterexamples { get; set; } = new();
    [JsonPropertyName("revisions")] public List<CasePackMaterial> Revisions { get; set; } = new();
}

public sealed class LoadedCasePack
{
    public required CasePackManifest Manifest { get; init; }
    public required List<MaterialInput> Baseline { get; init; }
    public required List<(CounterexampleCase Spec, List<MaterialInput> Materials)> Counterexamples { get; init; }
    public required List<MaterialInput> Revisions { get; init; }
}

public static class CasePackLoader
{
    public static string LocateRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "casepack", "manifest.json");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        // fall back to working directory
        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "casepack");
        if (File.Exists(Path.Combine(cwd, "manifest.json"))) return cwd;
        throw new FileNotFoundException("casepack/manifest.json not found");
    }

    public static LoadedCasePack Load(string root)
    {
        var manifest = JsonSerializer.Deserialize<CasePackManifest>(
            File.ReadAllText(Path.Combine(root, "manifest.json")))!;
        List<MaterialInput> ReadAll(List<CasePackMaterial> ms) => ms.Select(m =>
            new MaterialInput(m.Id, m.Kind, File.ReadAllText(Path.Combine(root, m.File)))).ToList();
        return new LoadedCasePack
        {
            Manifest = manifest,
            Baseline = ReadAll(manifest.Baseline),
            Counterexamples = manifest.Counterexamples.Select(c => (c, ReadAll(c.Materials))).ToList(),
            Revisions = ReadAll(manifest.Revisions)
        };
    }
}

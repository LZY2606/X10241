using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using IsoMix.Cases;

namespace IsoMix.App;

public static class CaseLoader
{
    public const string ResourceName = "IsoMix.Cases.source-composition-lab.json";

    public static CasePack LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException($"嵌入案例包缺失：{ResourceName}");
        return JsonSerializer.Deserialize<CasePack>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }

    public static string LoadEmbeddedText()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

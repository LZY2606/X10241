using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Storage;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Ser(object value) => JsonSerializer.Serialize(value, Options);
    public static T Deser<T>(string text) => JsonSerializer.Deserialize<T>(text, Options)!;
}

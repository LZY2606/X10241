using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IsotopeMix.Storage;

public static class JsonIo
{
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WriteOptions);
    public static T? Deserialize<T>(string text) => JsonSerializer.Deserialize<T>(text);
    public static JsonObject ParseObject(string text) => JsonNode.Parse(text)!.AsObject();

    // Deterministic canonical form: property names sorted recursively.
    public static string Canonical(JsonNode node)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteCanonical(writer, node);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var prop in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Key);
                    WriteCanonical(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray arr:
                writer.WriteStartArray();
                foreach (var item in arr) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValue val:
                var element = val.GetValue<JsonElement>();
                switch (element.ValueKind)
                {
                    case JsonValueKind.Number:
                        if (element.TryGetInt64(out long l)) writer.WriteNumberValue(l);
                        else writer.WriteNumberValue(element.GetDouble());
                        break;
                    case JsonValueKind.True: writer.WriteBooleanValue(true); break;
                    case JsonValueKind.False: writer.WriteBooleanValue(false); break;
                    case JsonValueKind.Null: writer.WriteNullValue(); break;
                    case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
                    default: element.WriteTo(writer); break;
                }
                break;
        }
    }

    public static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

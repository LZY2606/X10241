using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsotopeApp.Storage;

/// <summary>
/// Stable content hashing. Identity is content-derived, so an export -> delete
/// -> import round trip preserves ids, ordering and provenance links exactly.
/// </summary>
public static class Hashing
{
    public static string Sha256Hex(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Sha256Hex(string text)
        => Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string ContentId(string kind, string canonicalJson)
        => $"{kind}-{Sha256Hex(Encoding.UTF8.GetBytes(kind + "\n" + canonicalJson))[..16]}";

    public static string Canonical(object value)
        => JsonSerializer.Serialize(value, Domain.JsonOpts.Default);
}

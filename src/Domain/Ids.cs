using System.Security.Cryptography;
using System.Text;

namespace IsotopeLab.Domain;

/// <summary>ULID-style monotonic identifier: 26 Crockford-base32 chars, prefixed by record kind.</summary>
public static class Ids
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly object Gate = new();
    private static readonly byte[] Randomness = RandomNumberGenerator.GetBytes(10);
    private static long _lastMs;

    public static string New(string prefix)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now <= _lastMs)
            {
                now = _lastMs + 1;
                Increment(Randomness, 9);
            }
            _lastMs = now;

            Span<char> chars = stackalloc char[26];
            var time = now;
            for (var i = 9; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(time % 32)];
                time /= 32;
            }
            for (var i = 0; i < 10; i++)
            {
                chars[10 + i * 2] = Alphabet[Randomness[i] >> 5];
                chars[11 + i * 2] = Alphabet[Randomness[i] & 31];
            }
            FillRandom(Randomness);
            return prefix + "_" + new string(chars);
        }
    }

    public static bool LooksLike(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains('_') && value.Length > 8;

    private static void Increment(byte[] bytes, int index)
    {
        while (index >= 0)
        {
            if (bytes[index] < byte.MaxValue) { bytes[index]++; return; }
            bytes[index] = 0;
            index--;
        }
    }

    private static void FillRandom(byte[] bytes) =>
        RandomNumberGenerator.Fill(bytes);

    public static string Sha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256Hex(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));
}

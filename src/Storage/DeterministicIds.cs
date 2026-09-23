using System.Security.Cryptography;
using System.Text;

namespace IsotopeMixing.Storage;

public static class DeterministicIds
{
    public static string Of(string prefix, string stableKey)
        => $"{prefix}_{ToHex(Encoding.UTF8.GetBytes(stableKey)).Substring(0, 16)}";

    public static string NewEventId(string branchId, long seq, string type)
        => $"evt_{ToHex(Encoding.UTF8.GetBytes($"{branchId}|{seq}|{type}")).Substring(0, 20)}";

    private static string ToHex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

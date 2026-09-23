using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsotopeMix.Core;

public static class RecordKinds
{
    public const string Sample = "sample";
    public const string EndmemberSet = "endmember-set";
    public const string Covariance = "covariance";
    public const string Fractionation = "fractionation";
    public const string Units = "units";
    public const string Prior = "prior";
}

/// <summary>An immutable raw record. New versions are new rows; payloads are never rewritten.</summary>
public sealed record RawRecord(
    string Id, int Version, string Kind, string MediaType,
    byte[] Payload, string Hash, string BatchId, DateTimeOffset ImportedAt)
{
    public string Ref => $"{Id}@{Version}";
    public string PayloadText => Encoding.UTF8.GetString(Payload);

    public static RawRecord Create(string id, int version, string kind, string mediaType, byte[] payload, string batchId, DateTimeOffset at)
    {
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return new RawRecord(id, version, kind, mediaType, payload, hash, batchId, at);
    }

    public JsonDocument Json => JsonDocument.Parse(Payload).RootElement.Clone();
}

public sealed record ImportBatch(string Id, DateTimeOffset StartedAt, string Status, string? Error)
{
    public const string Committed = "committed";
    public const string RolledBack = "rolled-back";
}

public sealed record Branch(string Id, string Name, string? ParentId, long BaseSeq, long CreatedSeq);

public sealed record MixEvent(
    long Seq, string Id, string BranchId, string Type,
    string PayloadJson, IReadOnlyList<string> DependsOn,
    DateTimeOffset CreatedAt, string? Supersedes)
{
    public T Payload<T>() => JsonSerializer.Deserialize<T>(PayloadJson)!;
}

// ---------- Parsed domain payloads (derived layer views over raw bytes) ----------

public sealed record AxisValue(string Axis, Quantity Q);

public sealed record SamplePayload(string Name, IReadOnlyList<AxisValue> Values);

public sealed record Endmember(string Id, string Name, IReadOnlyList<AxisValue> Values);

public sealed record EndmemberSetPayload(string Name, IReadOnlyList<Endmember> Members);

public sealed record CovariancePayload(IReadOnlyList<string> Axes, string Unit, double[,] Matrix);

public sealed record FractionationPayload(string Label, IReadOnlyDictionary<string, Quantity> Deltas);

public sealed record UnitsPayload(IReadOnlyDictionary<string, (string Unit, string Basis)> Canonical);

public sealed record PriorPayload(string Kind, IReadOnlyDictionary<string, (double Lo, double Hi)> Box);

public static class PayloadParser
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static SamplePayload ParseSample(RawRecord r)
    {
        var d = JsonDocument.Parse(r.Payload).RootElement;
        return new SamplePayload(d.GetProperty("name").GetString()!, ReadAxes(d));
    }

    public static EndmemberSetPayload ParseEndmembers(RawRecord r)
    {
        var d = JsonDocument.Parse(r.Payload).RootElement;
        var members = d.GetProperty("members").EnumerateArray().Select(m =>
            new Endmember(m.GetProperty("id").GetString()!, m.GetProperty("name").GetString()!, ReadAxes(m)))
            .ToList();
        return new EndmemberSetPayload(d.GetProperty("name").GetString()!, members);
    }

    public static CovariancePayload ParseCovariance(RawRecord r)
    {
        var d = JsonDocument.Parse(r.Payload).RootElement;
        var axes = d.GetProperty("axes").EnumerateArray().Select(a => a.GetString()!).ToList();
        var rows = d.GetProperty("matrix").EnumerateArray().ToList();
        var m = new double[rows.Count, rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var cols = rows[i].EnumerateArray().ToList();
            for (int j = 0; j < cols.Count; j++) m[i, j] = cols[j].GetDouble();
        }
        return new CovariancePayload(axes, d.GetProperty("unit").GetString()!, m);
    }

    public static FractionationPayload ParseFractionation(RawRecord r)
    {
        var d = JsonDocument.Parse(r.Payload).RootElement;
        var deltas = new Dictionary<string, Quantity>();
        foreach (var e in d.GetProperty("deltas").EnumerateObject())
            deltas[e.Name] = ReadQuantity(e.Value);
        return new FractionationPayload(d.GetProperty("label").GetString()!, deltas);
    }

    public static UnitsPayload ParseUnits(RawRecord r)
    {
        var d = JsonDocument.Parse(r.Payload).RootElement;
        var map = new Dictionary<string, (string, string)>();
        foreach (var e in d.GetProperty("canonical").EnumerateObject())
            map[e.Name] = (e.Value.GetProperty("unit").GetString()!, e.Value.GetProperty("basis").GetString()!);
        return new UnitsPayload(map);
    }

    public static PriorPayload ParsePrior(RawRecord r)
    {
        var d = JsonDocument.Parse(r.Payload).RootElement;
        var box = new Dictionary<string, (double, double)>();
        foreach (var e in d.GetProperty("box").EnumerateObject())
            box[e.Name] = (e.Value.GetProperty("lo").GetDouble(), e.Value.GetProperty("hi").GetDouble());
        return new PriorPayload(d.GetProperty("kind").GetString()!, box);
    }

    private static List<AxisValue> ReadAxes(JsonElement el) =>
        el.GetProperty("values").EnumerateArray().Select(v =>
            new AxisValue(v.GetProperty("axis").GetString()!, ReadQuantity(v))).ToList();

    private static Quantity ReadQuantity(JsonElement v) => new(
        v.GetProperty("value").GetDouble(),
        v.GetProperty("unit").GetString()!,
        v.TryGetProperty("basis", out var b) ? b.GetString() ?? "unspecified" : "unspecified");
}

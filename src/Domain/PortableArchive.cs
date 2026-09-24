using System.Security.Cryptography;
using System.Text.Json;

namespace IsotopeMix.Domain;

/// <summary>
/// 可移植档案：事件流 + 原始字节 blob 一起导出。所有 ID 均为内容决定的
/// UUIDv5，因此删除本地库重新导入后身份、顺序与来源关系完全保持。
/// </summary>
public sealed class PortableArchive
{
    public string Format { get; set; } = "isotope-mix-portable/1";
    public string ExportedAt { get; set; } = "";
    public List<PortableEvent> Events { get; set; } = new();
    public List<PortableBlob> Blobs { get; set; } = new();
}

public sealed class PortableEvent
{
    public long Seq { get; set; }
    public string EventId { get; set; } = "";
    public string Type { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string Payload { get; set; } = "";
    public string At { get; set; } = "";
}

public sealed class PortableBlob
{
    public string BlobId { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Base64 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Origin { get; set; } = "";
}

public static class ArchivePort
{
    public static PortableArchive Export(EventStore store, DateTime at)
    {
        var archive = new PortableArchive { ExportedAt = at.ToString("yyyy-MM-ddTHH:mm:ssZ") };
        foreach (var e in store.AllEvents())
            archive.Events.Add(new PortableEvent
            {
                Seq = e.Seq, EventId = e.EventId, Type = e.Type,
                BranchId = e.BranchId, Payload = e.PayloadJson, At = e.At
            });

        var blobIds = archive.Events
            .Select(e => JsonDocument.Parse(e.Payload).RootElement)
            .SelectMany(root =>
            {
                var ids = new List<string>();
                if (root.TryGetProperty("rawPayloadBlobId", out var b) && b.ValueKind == JsonValueKind.String)
                    ids.Add(b.GetString()!);
                return ids;
            }).Distinct();
        foreach (var id in blobIds)
        {
            var bytes = store.GetBlob(id);
            if (bytes is null) continue;
            archive.Blobs.Add(new PortableBlob
            {
                BlobId = id,
                MediaType = "application/octet-stream",
                Base64 = Convert.ToBase64String(bytes),
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                Origin = ""
            });
        }
        return archive;
    }

    public static void Import(EventStore store, PortableArchive archive)
    {
        var blobs = new List<BlobWrite>();
        foreach (var b in archive.Blobs)
        {
            var bytes = Convert.FromBase64String(b.Base64);
            blobs.Add(new BlobWrite(b.BlobId, b.MediaType, bytes, b.Sha256,
                archive.ExportedAt, b.Origin));
        }
        var events = archive.Events.OrderBy(e => e.Seq).Select(e =>
            new EventWrite(e.EventId, e.Type, e.BranchId, e.Payload, e.At)).ToList();
        store.AppendInTransaction(blobs, events);
    }
}

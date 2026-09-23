using System.Text.Json.Nodes;

namespace IsotopeMix.Model;

// An imported file is stored byte-for-byte. Re-interpretation never rewrites it.
public sealed class RawBlob
{
    public string BlobId { get; set; } = "";      // sha256(content)
    public string FileName { get; set; } = "";
    public string MediaType { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class ImportBatch
{
    public string BatchId { get; set; } = "";
    public string Label { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

// A typed fact parsed from a blob. payload_json holds the full domain document.
// The fact is never mutated: a new re-interpretation creates a new version row.
public sealed class FactRecord
{
    public long RowId { get; set; }
    public string StableId { get; set; } = "";
    public string Kind { get; set; } = "";        // sample | source | fractionation | prior | unit
    public int Version { get; set; }
    public string BatchId { get; set; } = "";
    public string BlobId { get; set; } = "";
    public string SourceRef { get; set; } = "";   // file + row locator inside the blob
    public string CreatedAt { get; set; } = "";
    public JsonObject Payload { get; set; } = new();
}

public sealed class Branch
{
    public string BranchId { get; set; } = "";
    public string Label { get; set; } = "";
    public string ParentEventId { get; set; } = ""; // event this branch forks from ("" for root)
    public string CreatedAt { get; set; } = "";
    public string HeadEventId { get; set; } = "";
}

public sealed class EventRecord
{
    public long Seq { get; set; }                  // global append order
    public string EventId { get; set; } = "";     // content hash, stable across export/import
    public string BranchId { get; set; } = "";
    public string ParentEventId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Actor { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public JsonObject Payload { get; set; } = new();
}

using System.Text.Json.Nodes;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Web;

/// <summary>
/// Every displayed value carries a stable identity: uid for entities, (branchId, seq, eventId)
/// for events, resultId for derived conclusions, plus provenance fingerprints.
/// </summary>
public sealed class StateView
{
    public required string DbPath { get; set; }
    public required int SchemaVersion { get; set; }
    public required List<object> Branches { get; set; }
    public required List<object> RawRecords { get; set; }
    public required List<object> Results { get; set; }
    public required List<object> Events { get; set; }
    public required List<object> Decisions { get; set; }
    public required List<object> Diagnostics { get; set; }
    public required ReplayInfo Replay { get; set; }
    public required double ConfidenceLevel { get; set; }
}

public sealed class ReplayInfo
{
    public required bool Ran { get; set; }
    public List<string> Log { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
}

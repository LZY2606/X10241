namespace IsoLab.Domain;

/// <summary>Raw imported material. Source bytes/JSON are kept verbatim; normalization happens only in derived layers.</summary>
public sealed record MaterialRow(
    string Id, string Kind, string Name, int Version, string? Supersedes,
    string RawJson, string? Unit, string? Coord, int Seq, string ImportBatch, string CreatedEvent);

public sealed record EventRow(
    string Id, string BranchId, string Kind, string PayloadJson, int Seq, string CreatedAt, string? ParentId);

public sealed record BranchRow(
    string Id, string Name, string? ParentBranch, string? HeadEvent, int Version, string Status);

public sealed record CandidateRow(
    string Id, string BranchId, string SourceEvent, string PropsJson, double Residual,
    int Component, string State, string DepsJson, int Stale);

public sealed record DecisionRow(
    string Id, string BranchId, string CandidateId, string Action, string Note, string EventId, int Undone);

public sealed record NewMaterial(string Kind, string Name, string RawJson, string? Unit, string? Coord);

public sealed class ImportException : Exception
{
    public IReadOnlyList<string> Diagnostics { get; }
    public ImportException(IEnumerable<string> diagnostics)
        : base("import failed: " + string.Join("; ", diagnostics)) => Diagnostics = diagnostics.ToArray();
}

public sealed class ConflictException : Exception
{
    public int ExpectedVersion { get; }
    public int CurrentVersion { get; }
    public IReadOnlyList<EventRow> InterveningEvents { get; }
    public ConflictException(int expected, int current, IReadOnlyList<EventRow> intervening)
        : base($"branch version conflict: expected {expected}, current {current}")
    {
        ExpectedVersion = expected;
        CurrentVersion = current;
        InterveningEvents = intervening;
    }
}

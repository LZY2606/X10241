using System.Text.Json;

namespace IsotopeApp.Domain;

public enum DecisionKind { Accepted, Rejected, ParameterChange, Undo, ConflictDetected }

/// <summary>
/// Append-only, machine-readable provenance event. Events are the only way the
/// system records judgments: import, recomputation, identifiability findings,
/// human decisions and conflicts. No event is ever mutated or deleted.
/// </summary>
public sealed class ProjectionEvent
{
    public required string EventId { get; init; }
    public required int Seq { get; init; }
    public required string BranchId { get; init; }
    public required string ParentEventId { get; init; }
    public required string Type { get; init; }
    public required string OccurredAt { get; init; }
    public string? Actor { get; init; }
    public required JsonDocument Payload { get; init; }

    /// <summary>Exact dependency versions visible at the time of the event.</summary>
    public required IReadOnlyList<DependencyRef> Dependencies { get; init; }

    /// <summary>Objects invalidated when this event replaced an input/parameter.</summary>
    public IReadOnlyList<string> InvalidatedObjectIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optimistic concurrency token: a decision carrying a stale head loses and
    /// gets a 409 with both sides of the diff instead of silently overwriting.
    /// </summary>
    public required string HeadHash { get; init; }
}

public sealed record DependencyRef(string Kind, string ObjectId, string VersionHash);

public sealed class Branch
{
    public required string BranchId { get; init; }
    public required string Name { get; init; }
    public required string? ParentBranchId { get; init; }
    public required string CreatedFromEventId { get; init; }
    public required string CreatedAt { get; init; }
    public bool Archived { get; set; }
}

/// <summary>
/// A human adjudication. Kept forever; undo creates a new event that restores
/// the previous head instead of deleting the original decision.
/// </summary>
public sealed class DecisionRecord
{
    public required string DecisionId { get; init; }
    public required string BranchId { get; init; }
    public required string TrialId { get; init; }
    public required DecisionKind Kind { get; init; }
    public required string EventId { get; init; }
    public string? Comment { get; init; }
    public required string At { get; init; }
    /// <summary>Decision this one revoked (for the undo chain).</summary>
    public string? UndoesDecisionId { get; init; }
}

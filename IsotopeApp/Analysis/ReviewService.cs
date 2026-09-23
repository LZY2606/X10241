using System.Text.Json;
using IsotopeApp.Domain;
using IsotopeApp.Storage;

namespace IsotopeApp.Analysis;

/// <summary>
/// Review/branch workflow: branch a reinterpretation, record accept/reject
/// decisions with optimistic concurrency, undo by appending a restoring event,
/// revise a parameter with scoped invalidation, and reconstruct the view at an
/// arbitrary past event without recomputing over history.
/// </summary>
public sealed class ReviewService
{
    private readonly Store _store;
    public ReviewService(Store store) { _store = store; }

    public string BranchForReinterpretation(string parentBranch, string fromEventId, string name)
    {
        string id = _store.CreateBranch(name, parentBranch, fromEventId);
        _store.AppendEvent(id, "branch_created", new { name, parentBranch, fromEventId },
            new[] { new DependencyRef("branch", parentBranch, fromEventId) }, Array.Empty<string>());
        return id;
    }

    public sealed record DecisionResult(ProjectionEvent Event, DecisionRecord Decision, string NewHead);

    public DecisionResult Decide(string branchId, string trialId, DecisionKind kind,
        string expectedHead, string? comment, string actor = "reviewer")
    {
        var prior = _store.LatestDecision(branchId, trialId);
        if (kind == DecisionKind.Undo && prior is null)
            throw new InvalidOperationException("nothing to undo");

        var trial = _store.GetTrialAt(branchId, trialId)
                    ?? throw new KeyNotFoundException(trialId);
        var deps = Invalidation.AllRefs(trial);
        var payload = new
        {
            trialId, decision = kind.ToString().ToLowerInvariant(),
            comment = comment ?? "",
            undoes = kind == DecisionKind.Undo ? prior?.DecisionId : null,
            priorDecision = prior?.DecisionId
        };
        // StaleHeadException propagates to the 409 conflict page.
        var ev = _store.AppendEvent(branchId, "decision_recorded", payload, deps,
            Array.Empty<string>(), expectedHead, actor);
        var decision = new DecisionRecord
        {
            DecisionId = Hashing.ContentId("dec", ev.EventId + kind),
            BranchId = branchId, TrialId = trialId, Kind = kind,
            EventId = ev.EventId, Comment = comment, At = ev.OccurredAt,
            UndoesDecisionId = kind == DecisionKind.Undo ? prior?.DecisionId : null
        };
        _store.AddDecision(decision);
        if (kind == DecisionKind.ParameterChange)
            InvalidateForChange(branchId, trialId, ev, deps);
        return new DecisionResult(ev, decision, ev.HeadHash);
    }

    /// <summary>
    /// Apply a parameter revision (new fractionation version). Only trials that
    /// referenced the old fractionation are invalidated; other conclusions stay
    /// valid and are never globally replaced.
    /// </summary>
    public (ProjectionEvent Event, string InvalidatedListJson) ReviseFractionation(
        string branchId, FractionationVersion newVersion, string oldId, string oldVersion)
    {
        _store.SaveFractionation(branchId, newVersion, oldVersion);
        var row = _store.ActiveFractionation(branchId, newVersion.FractionationId);
        var trials = _store.LatestTrials(branchId);
        var scope = Invalidation.Compute(trials,
            new[] { new Invalidation.Change("fractionation", oldId, oldVersion, row.Version) });
        var ev = _store.AppendEvent(branchId, "parameter_revision", new
        {
            kind = "fractionation", oldId, oldVersion,
            newId = row.Id, newVersion = row.Version,
            invalidatedTrials = scope.TrialIds,
            scope = "trials_using_old_version_only"
        }, new[] { new DependencyRef("fractionation", oldId, oldVersion),
                   new DependencyRef("fractionation", row.Id, row.Version) },
        scope.TrialIds);
        return (ev, Hashing.Canonical(scope));
    }

    /// <summary>
    /// One direct source-data change. Only trials referencing that source
    /// version fall in the invalidation scope.
    /// </summary>
    public (ProjectionEvent Event, IReadOnlyList<string> Affected) ChangeSource(
        string branchId, string sourceId, string oldVersion, string newVersion)
    {
        var trials = _store.LatestTrials(branchId);
        var scope = Invalidation.Compute(trials,
            new[] { new Invalidation.Change("source", sourceId, oldVersion, newVersion) });
        var ev = _store.AppendEvent(branchId, "source_data_changed", new
        {
            sourceId, oldVersion, newVersion,
            invalidatedTrials = scope.TrialIds,
            rule = "scoped_invalidation_not_global_replace"
        }, new[] { new DependencyRef("source", sourceId, oldVersion),
                   new DependencyRef("source", sourceId, newVersion) },
        scope.TrialIds);
        return (ev, scope.TrialIds);
    }

    private void InvalidateForChange(string branchId, string trialId, ProjectionEvent ev, IReadOnlyList<DependencyRef> deps)
    {
        _store.AppendEvent(branchId, "invalidated_scope_computed", new
        {
            trialId, fromEvent = ev.EventId, affected = new[] { trialId }
        }, deps, new[] { trialId });
    }

    /// <summary>
    /// Reconstruct the review state visible at an event. The stored trial result
    /// captured then is returned as-is; later computations never overwrite it.
    /// </summary>
    public ProjectionEvent ViewAt(string branchId, string eventId)
    {
        var ev = _store.GetEvent(eventId);
        var upTo = _store.ListEvents(branchId, ev.Seq);
        return ev;
    }
}

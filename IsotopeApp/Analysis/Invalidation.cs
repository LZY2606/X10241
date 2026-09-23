using IsotopeApp.Domain;
using IsotopeApp.Storage;

namespace IsotopeApp.Analysis;

/// <summary>
/// Dependency-graph invalidation. When one source/fractionation/prior changes,
/// only the trials that referenced that exact (object, version) are marked
/// stale; conclusions using untouched inputs remain valid and are never
/// globally replaced.
/// </summary>
public static class Invalidation
{
    public readonly record struct Change(string Kind, string ObjectId, string OldVersion, string? NewVersion);

    public sealed record Scope(
        IReadOnlyList<string> TrialIds,
        IReadOnlyList<DependencyRef> BrokenDependencies);

    public static Scope Compute(IEnumerable<StoredTrial> trials, IEnumerable<Change> changes)
    {
        var affected = new List<string>();
        var broken = new List<DependencyRef>();
        var changeByKey = changes.ToDictionary(c => (c.Kind, c.ObjectId));
        foreach (var trial in trials)
        {
            var refs = AllRefs(trial);
            var hit = refs.FirstOrDefault(r =>
                changeByKey.TryGetValue((r.Kind, r.ObjectId), out var c) &&
                c.OldVersion == r.VersionHash);
            if (hit.Kind is not null)
            {
                affected.Add(trial.TrialId);
                if (!broken.Contains(hit)) broken.Add(hit);
            }
        }
        return new Scope(affected, broken);
    }

    public static IReadOnlyList<DependencyRef> AllRefs(StoredTrial t)
    {
        var refs = new List<DependencyRef>(t.SourceVersions)
        {
            new("sample", t.SampleId, t.SampleVersion),
            new("fractionation", t.FractionationId, t.FractionationVersion)
        };
        if (t.PriorId is not null && t.PriorVersion is not null)
            refs.Add(new DependencyRef("prior", t.PriorId, t.PriorVersion));
        if (t.CovarianceVersion is not null)
            refs.Add(new DependencyRef("covariance", t.SampleId, t.CovarianceVersion));
        return refs;
    }
}

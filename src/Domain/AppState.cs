using System.Text.Json;

namespace IsotopeMix.Domain;

public sealed class BranchState
{
    public BranchCreated Spec { get; set; } = null!;
    public long Version { get; set; }       // 分支内写入次数（乐观并发）
    public long LastSeq { get; set; }
    public List<AnalysisComputed> Analyses { get; } = new();
    public List<DecisionMade> Decisions { get; } = new();
    public bool Superseded { get; set; }
    public string? SupersededBy { get; set; }
}

public sealed class AnalysisView
{
    public AnalysisComputed Analysis { get; set; } = null!;
    public bool Stale { get; set; }
    public string? StaleBecause { get; set; }
}

public sealed class FoldedState
{
    public List<RawBatchImported> Batches { get; } = new();
    public Dictionary<string, BranchState> Branches { get; } = new();
    public List<InvalidationBulletin> Bulletins { get; } = new();
    public List<CounterexampleRecorded> Counterexamples { get; } = new();
    public List<StoredEvent> Events { get; } = new();

    public BranchState? Branch(string id) => Branches.GetValueOrDefault(id);
}

public static class StateFolder
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOpts)!;

    public static FoldedState Fold(IEnumerable<StoredEvent> events, long? asOfSeq = null)
    {
        var state = new FoldedState();
        foreach (var e in events)
        {
            if (asOfSeq.HasValue && e.Seq > asOfSeq.Value) break;
            state.Events.Add(e);
            switch (e.Type)
            {
                case "raw-batch-imported":
                    state.Batches.Add(Deserialize<RawBatchImported>(e.PayloadJson));
                    break;
                case "branch-created":
                {
                    var p = Deserialize<BranchCreated>(e.PayloadJson);
                    state.Branches[p.BranchId] = new BranchState { Spec = p, LastSeq = e.Seq };
                    break;
                }
                case "analysis-computed":
                {
                    var p = Deserialize<AnalysisComputed>(e.PayloadJson);
                    if (state.Branches.TryGetValue(p.BranchId, out var b))
                    {
                        b.Analyses.Add(p);
                        b.LastSeq = e.Seq;
                    }
                    break;
                }
                case "decision-made":
                {
                    var p = Deserialize<DecisionMade>(e.PayloadJson);
                    if (state.Branches.TryGetValue(p.BranchId, out var b))
                    {
                        b.Decisions.Add(p);
                        b.Version++;
                        b.LastSeq = e.Seq;
                    }
                    break;
                }
                case "decision-reverted":
                {
                    var p = Deserialize<DecisionReverted>(e.PayloadJson);
                    if (state.Branches.TryGetValue(p.BranchId, out var b))
                    {
                        var target = b.Decisions.FirstOrDefault(d => d.DecisionId == p.RevertedDecisionId);
                        if (target is not null)
                        {
                            b.Decisions.Remove(target);
                            b.Version++;
                            b.LastSeq = e.Seq;
                        }
                    }
                    break;
                }
                case "parameter-revision":
                {
                    var p = Deserialize<ParameterRevision>(e.PayloadJson);
                    if (state.Branches.TryGetValue(p.BranchId, out var old)) old.Superseded = true;
                    if (state.Branches.TryGetValue(p.NewBranchId, out var nb)) nb.LastSeq = e.Seq;
                    break;
                }
                case "source-revision":
                {
                    var p = Deserialize<SourceRevision>(e.PayloadJson);
                    if (state.Branches.TryGetValue(p.BranchId, out var old)) old.Superseded = true;
                    if (state.Branches.TryGetValue(p.NewBranchId, out var nb)) nb.LastSeq = e.Seq;
                    break;
                }
                case "invalidation-bulletin":
                    state.Bulletins.Add(Deserialize<InvalidationBulletin>(e.PayloadJson));
                    break;
                case "counterexample-recorded":
                    state.Counterexamples.Add(Deserialize<CounterexampleRecorded>(e.PayloadJson));
                    break;
            }
        }

        // 根据失效公告标注陈旧分析（不删除、不覆盖旧结论）。
        foreach (var bulletin in state.Bulletins)
        foreach (var branch in state.Branches.Values)
        {
            var bseq = long.Parse(branch.Spec.BaseEventSeq);
            foreach (var a in branch.Analyses.ToList())
            {
                // 失效公告影响的是公告发生之前、引用了被替换引用的分支分析。
                if (bulletin.StaleAnalysisIds.Contains(a.AnalysisId))
                {
                    // 占位：真正标注在视图层按分析自身引用判断。
                }
            }
        }
        return state;
    }
}

using System.Text.Json.Nodes;
using IsotopeMixing.Cases;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Engine;

/// <summary>
/// Runs the case pack's narrative as one continuous story: initial import -> reviewer checks
/// -> boundary counterexamples on branches -> focused near-collinear branch -> parameter
/// revision -> local source change -> conflicting human decisions.
/// </summary>
public sealed class ReplayRunner
{
    private readonly Repository _repo;
    private readonly BranchStore _branches;
    private readonly ScenarioEngine _engine;
    private readonly DecisionService _decisions;
    private readonly ArchiveStore _archives;

    public ReplayRunner(Repository repo, BranchStore branches, ScenarioEngine engine,
        DecisionService decisions, ArchiveStore archives)
    {
        _repo = repo; _branches = branches; _engine = engine;
        _decisions = decisions; _archives = archives;
    }

    public sealed record ReplayReport(string MainBranch, List<string> CounterBranches,
        List<string> Events, List<string> Conflicts, string FocusedBranch);

    public ReplayReport Run(CaseFile file, string batchId, double confidence)
    {
        var events = new List<string>();
        var conflicts = new List<string>();
        string main = "br-main";
        var rawScenarios = _repo.ReadRawByBatch(batchId).Where(r => r.kind == "scenario").Select(r => r.uid).ToList();

        if (_branches.ListBranches().All(b => b.BranchId != main))
        {
            _branches.CreateBranch(main, null, "主干：初始导入与基线复核", "baseline review", batchId);
        }

        // Baseline: evaluate every scenario once.
        foreach (var uid in rawScenarios)
        {
            if (_branches.Events(main).Any(e => e.EventType == "scenario_evaluated"
                && e.Payload["scenarioId"]?.GetValue<string>() == uid)) continue;
            var c = _engine.EvaluateScenario(main, batchId, uid, confidence);
            events.Add($"{main}:{uid}:{c.ResultIds.Count} candidates");
        }

        var counterBranches = new List<string>();
        var scenarioByTag = new Dictionary<string, string>();
        foreach (var sc in file.Scenarios)
            foreach (var t in sc.Tags ?? new()) scenarioByTag[t] = sc.Uid;

        string baselineEventId = _branches.Events(main).First().EventId;

        // Counterexample archive: one branch per boundary situation, each changes exactly one source.
        foreach (var tag in new[] { "near-collinear", "outside-envelope", "correlated-error",
                                     "missing-dimension", "fractionation-switch", "multiple-regions", "infeasible-prior" })
        {
            if (!scenarioByTag.TryGetValue(tag, out var uid)) continue;
            string branchId = $"br-counter-{tag}";
            if (_branches.ListBranches().All(b => b.BranchId != branchId))
            {
                _branches.CreateBranch(branchId, main, $"反例：{tag}", $"single-source change: {tag}", batchId);
                _branches.AppendEvent(branchId, null, "counterexample_started", new JsonObject
                {
                    ["kind"] = "counterexample_started",
                    ["tag"] = tag,
                    ["baselineEventId"] = baselineEventId,
                    ["changedSource"] = CounterChangeTarget(tag)
                });
            }
            if (!_branches.Events(branchId).Any(e => e.EventType == "scenario_evaluated"))
            {
                if (tag == "fractionation-switch")
                {
                    var sc = file.Scenarios.First(s => s.Uid == uid);
                    _engine.RecordRevision(branchId, "parameter", sc.Fractionation ?? "frac-v1", new JsonObject
                    {
                        ["scope"] = "fractionation", ["scenario"] = uid, ["fractionation"] = "frac-v2"
                    });
                }
                var comp = _engine.EvaluateScenario(branchId, batchId, uid, confidence);
                events.Add($"{branchId}:{uid}:{comp.ResultIds.Count}");
                // Reviewer accepts or rejects; both outcomes are retained.
                var result = comp.ResultIds[0];
                _decisions.Decide(branchId, uid, result,
                    tag is "outside-envelope" or "infeasible-prior" ? "reject" : "accept",
                    $"counterexample review {tag}");
                _archives.Add(DeterministicIds.Of("arc", $"{branchId}|{tag}"), branchId,
                    $"反例档案 {tag}", DateTime.UtcNow.ToString("o"), baselineEventId,
                    CounterChangeTarget(tag), tag,
                    tag is "outside-envelope" or "infeasible-prior" ? null : result,
                    tag is "outside-envelope" or "infeasible-prior" ? result : null);
            }
            counterBranches.Add(branchId);
        }

        // Focused branch: near-collinear, retain ALL legal candidates (structural ambiguity if any).
        string focused = "br-focus-collinear";
        if (_branches.ListBranches().All(b => b.BranchId != focused) && scenarioByTag.TryGetValue("near-collinear", out var nearUid))
        {
            _branches.CreateBranch(focused, main, "专查：近共线可识别性", "identifiability deep dive", batchId);
            var comp = _engine.EvaluateScenario(focused, batchId, nearUid, confidence);
            events.Add($"{focused}:{nearUid}:candidates={comp.ResultIds.Count}");
        }

        // Parameter revision (fractionation) affecting a specific scenario.
        if (scenarioByTag.TryGetValue("fractionation-switch", out var fsUid)
            && !_branches.Events(main).Any(e => e.EventType == "parameter_revised"))
        {
            var (_, _, affected, scenarios) = _engine.RecordRevision(main, "parameter", "frac-v1", new JsonObject
            {
                ["scope"] = "fractionation", ["scenario"] = fsUid, ["fractionation"] = "frac-v2",
                ["note"] = "分馏版本切换 v1 -> v2，仅失效引用 frac-v1 的结论"
            });
            events.Add($"parameter revision invalidated {affected.Count} result(s) in {scenarios.Count} scenario(s)");
            _engine.EvaluateScenario(main, batchId, fsUid, confidence);
        }

        // Local source change affecting only dependent results.
        if (scenarioByTag.TryGetValue("correlated-error", out var ceUid)
            && !_branches.Events(main).Any(e => e.EventType == "source_revised"))
        {
            var sc = file.Samples.First(s => s.Uid == file.Scenarios.First(x => x.Uid == ceUid).Sample);
            var (_, _, affected, _) = _engine.RecordRevision(main, "source", sc.Uid, new JsonObject
            {
                ["scope"] = "local",
                ["axis"] = file.Axes[0].Key,
                ["sigma"] = 1.4,
                ["note"] = "局部源数据改动：只影响依赖该样品的结论"
            });
            events.Add($"local source change invalidated {affected.Count} result(s)");
            _engine.EvaluateScenario(main, batchId, ceUid, confidence);
        }

        // Conflicting human decisions on the same branch (two windows).
        if (scenarioByTag.TryGetValue("multiple-regions", out var mrUid))
        {
            var headBefore = _branches.HeadSeq(main);
            _decisions.Decide(main, mrUid, null, "accept", "窗口A：接受第一可行区", expectedSeq: headBefore + 1);
            try
            {
                _decisions.Decide(main, mrUid, null, "reject", "窗口B：基于旧序号提交否定", expectedSeq: headBefore + 1);
            }
            catch (ConflictException ex)
            {
                conflicts.Add(ex.Message);
            }
        }

        return new ReplayReport(main, counterBranches, events, conflicts, focused);
    }

    private static string CounterChangeTarget(string tag) => tag switch
    {
        "near-collinear" => "ems-collinear",
        "outside-envelope" => "sm-far",
        "correlated-error" => "cov-correlated",
        "missing-dimension" => "sm-missing-n",
        "fractionation-switch" => "frac-v1",
        "multiple-regions" => "prior-structural",
        "infeasible-prior" => "prior-impossible",
        _ => "unknown"
    };
}

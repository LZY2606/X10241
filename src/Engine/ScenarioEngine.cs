using System.Text.Json.Nodes;
using IsotopeMixing.Cases;
using IsotopeMixing.Solver;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Engine;

public sealed class ScenarioEngine
{
    private readonly Repository _repo;
    private readonly BranchStore _branches;
    private readonly ResultStore _results;
    private readonly InterpretationService _interp;

    public ScenarioEngine(Repository repo, BranchStore branches, ResultStore results, InterpretationService interp)
    {
        _repo = repo; _branches = branches; _results = results; _interp = interp;
    }

    public sealed record Computed(string EventId, long Seq, List<string> ResultIds);

    public Computed EvaluateScenario(string branchId, string batchId, string scenarioUid, double confidence,
        long? expectedSeq = null)
    {
        var events = _branches.Events(branchId);
        var pc = _interp.Project(batchId, events);
        var sc = pc.Scenarios[scenarioUid];
        var scenario = _interp.BuildScenario(pc, sc, confidence);
        var solver = new MixingSolver();
        var outputs = solver.Solve(scenario);

        var resultIds = new List<string>();
        string? firstEventId = null;
        long firstSeq = -1;
        for (int idx = 0; idx < outputs.Count; idx++)
        {
            var output = outputs[idx];
            string resultId = DeterministicIds.Of("res",
                $"{branchId}|{scenarioUid}|{scenarioUid}:{idx}|{events.Count}|{confidence}");
            resultId = $"res_{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{branchId}|{scenarioUid}|g{output.StructuralGroupIndex ?? "00"}|h{events.Count}")))
                .ToLowerInvariant().Substring(0, 18)}";
            var deps = Dependencies(pc, sc, batchId);
            var row = new ResultRow
            {
                ResultId = resultId, BranchId = branchId, CreatedEventId = "(pending)",
                CreatedSeq = -1, ScenarioId = scenarioUid, ScenarioLabel = sc.Label,
                SampleUid = scenario.Sample.Uid, EndmemberSetId = scenario.Endmembers.Uid,
                CovUid = sc.Covariance ?? scenario.Sample.Uid + "-cov",
                FracVersionId = scenario.Fractionation?.Uid, PriorId = scenario.Prior?.Uid,
                Unit = string.Join(",", scenario.Sample.Units), Datum = string.Join(",", scenario.Sample.Datums),
                Status = output.Status, Identifiability = output.Identifiability, Feasible = output.Feasible,
                Dof = output.Dof, Chi2 = output.Chi2, PValue = output.PValue,
                BestWeightsJson = output.BestWeights is null ? null : Json.Ser(output.BestWeights),
                ResidualJson = output.Residuals is null ? null : Json.Ser(output.Residuals),
                RegionJson = Json.Ser(new
                {
                    polygon = output.Polygon, line = output.Line, unresolved = output.Unresolved,
                    endmembers = scenario.Endmembers.Members.Select(m => new { m.Uid, m.Label }).ToArray()
                }),
                AssessmentsJson = Json.Ser(output.Assessments),
                InputsJson = Json.Ser(new
                {
                    sample = scenario.Sample.Uid, endmemberSet = scenario.Endmembers.Uid,
                    fractionation = scenario.Fractionation?.Uid, prior = scenario.Prior?.Uid,
                    confidence, structuralGroup = output.StructuralGroupIndex
                })
            };
            // Event carries machine-readable judgments; result row is the derived view at that event.
            var payload = new JsonObject
            {
                ["kind"] = "scenario_evaluated",
                ["scenarioId"] = scenarioUid,
                ["scenarioLabel"] = sc.Label,
                ["resultId"] = resultId,
                ["status"] = output.Status,
                ["identifiability"] = output.Identifiability,
                ["feasible"] = output.Feasible,
                ["candidateIndex"] = idx,
                ["candidateCount"] = outputs.Count,
                ["assessments"] = JsonNode.Parse(Json.Ser(output.Assessments))
            };
            var (seq, eid) = _branches.AppendEvent(branchId, idx == 0 ? expectedSeq : null,
                "scenario_evaluated", payload, producedResultId: resultId);
            if (idx == 0) { firstEventId = eid; firstSeq = seq; }
            row.CreatedEventId = eid;
            row.CreatedSeq = seq;
            _results.Insert(row, deps);
            resultIds.Add(resultId);
        }
        return new Computed(firstEventId!, firstSeq, resultIds);
    }

    public List<(string uid, string kind, string fingerprint)> Dependencies(
        InterpretationService.ProjectedCase pc, ScenarioFile sc, string batchId)
    {
        var deps = new List<(string, string, string)>();
        void Add(string uid, string kind)
            => deps.Add((uid, kind, _interp.Fingerprint(uid, batchId, pc.Revisions)));
        Add(sc.Sample, "sample");
        Add(sc.EndmemberSet, "endmember_set");
        var set = pc.Sets[sc.EndmemberSet];
        foreach (var m in set.Members) Add(m, "endmember");
        var covUid = sc.Covariance ?? pc.Samples[sc.Sample].Covariance;
        if (covUid is not null && pc.Covariances.ContainsKey(covUid)) Add(covUid, "covariance");
        if (sc.Fractionation is not null) Add(sc.Fractionation, "fractionation");
        if (sc.Prior is not null) Add(sc.Prior, "prior");
        foreach (var rev in pc.Revisions) Add(rev.TargetUid, rev.Kind == "source" ? "source_revision" : "parameter_revision");
        return deps;
    }

    /// <summary>Record a source/parameter revision, compute the invalidation scope, and mark only those results.</summary>
    public (string eventId, long seq, List<string> invalidatedResults, List<string> affectedScenarios) RecordRevision(
        string branchId, string kind, string targetUid, JsonNode payload, long? expectedSeq = null)
    {
        payload["target"] = targetUid;
        payload["scope"] = payload["scope"] ?? (kind == "source" ? "local" : "parameter");
        var (seq, eid) = _branches.AppendEvent(branchId, expectedSeq,
            kind == "source" ? "source_revised" : "parameter_revised", payload);
        var invalidated = _results.InvalidateByDependency(new[] { targetUid }, eid);
        var scope = new JsonObject
        {
            ["kind"] = "dependencies_invalidated",
            ["changedSource"] = targetUid,
            ["eventId"] = eid,
            ["scope"] = payload["scope"]?.GetValue<string>(),
            ["affectedResults"] = new JsonArray(invalidated.Select(x => JsonNode.Parse(Json.Ser(new { resultId = x.Item1, scenarioId = x.Item2 }))).ToArray()),
            ["note"] = "Only results depending on this fingerprint are invalidated; older result rows and events are never replaced."
        };
        _branches.AppendEvent(branchId, null, "dependencies_invalidated", scope);
        return (eid, seq, invalidated.Select(x => x.Item1).ToList(), invalidated.Select(x => x.Item2).Distinct().ToList());
    }
}

using System.Text.Json.Nodes;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Engine;

public sealed class DecisionService
{
    private readonly BranchStore _branches;
    private readonly DecisionStore _decisions;

    public DecisionService(BranchStore branches, DecisionStore decisions)
    {
        _branches = branches; _decisions = decisions;
    }

    public (string decisionId, string eventId, long seq) Decide(string branchId, string scenarioId,
        string? resultId, string verdict, string rationale, long? expectedSeq = null)
    {
        if (verdict is not ("accept" or "reject" or "adjust"))
            throw new ArgumentException("verdict must be accept|reject|adjust");
        var (seq, eid) = _branches.AppendEvent(branchId, expectedSeq, "decision_made", new JsonObject
        {
            ["kind"] = "decision_made",
            ["scenarioId"] = scenarioId,
            ["resultId"] = resultId,
            ["verdict"] = verdict,
            ["rationale"] = rationale
        });
        string decisionId = DeterministicIds.Of("dec", $"{branchId}|{seq}|{scenarioId}|{verdict}");
        _decisions.Add(new DecisionRow
        {
            DecisionId = decisionId, BranchId = branchId, EventId = eid, Seq = seq,
            ScenarioId = scenarioId, ResultId = resultId, Verdict = verdict,
            Rationale = rationale, At = DateTime.UtcNow.ToString("o")
        });
        return (decisionId, eid, seq);
    }

    /// <summary>Undo restores the previous branch state by revoking; original data is never modified.</summary>
    public string Revoke(string branchId, string decisionId, long? expectedSeq = null)
    {
        var d = _decisions.Get(decisionId) ?? throw new InvalidOperationException("decision not found");
        if (d.Revoked) throw new InvalidOperationException("decision already revoked");
        var (seq, eid) = _branches.AppendEvent(branchId, expectedSeq, "decision_revoked", new JsonObject
        {
            ["kind"] = "decision_revoked",
            ["decisionId"] = decisionId,
            ["undoOfEventId"] = d.EventId,
            ["scenarioId"] = d.ScenarioId
        }, undoOfEventId: d.EventId);
        _decisions.Revoke(decisionId, eid);
        return eid;
    }
}

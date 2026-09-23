using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed partial class Store
{
    /// <summary>在分支上追加人工裁定。不改原数据；返回乐观冲突信息（两窗口同时保存）。</summary>
    public (bool ok, string? conflictWith, string? eventId, string? decisionId) RecordDecision(
        string branchId, string expectedHead, string kind, string? targetCandidate,
        string rationale, List<string> deps, string? supersedesDecision = null)
    {
        string type = kind switch
        {
            "accept" => EventTypes.DecisionAccepted,
            "reject" => EventTypes.DecisionRejected,
            "adjust" => EventTypes.DecisionAdjusted,
            "undo" => EventTypes.DecisionUndone,
            _ => EventTypes.DecisionAccepted
        };
        var (ok, conflictWith, eventId) = AppendWithConcurrency(branchId, expectedHead, type,
            new { kind, targetCandidate, rationale, supersedes = supersedesDecision },
            currentRuleVersion(branchId), deps, new List<string>());
        if (!ok) return (false, conflictWith, null, null);

        string decisionId = "dec." + Guid.NewGuid().ToString("N").Substring(0, 10);
        using var tx = Conn.BeginTransaction();
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO decisions(decision_id,branch_id,event_id,kind,target_candidate,rationale,supersedes_decision,created_at)
                            VALUES(@id,@b,@e,@k,@c,@r,@s,@t);";
        Add(cmd, "@id", decisionId); Add(cmd, "@b", branchId); Add(cmd, "@e", eventId!);
        Add(cmd, "@k", kind); Add(cmd, "@c", targetCandidate); Add(cmd, "@r", rationale);
        Add(cmd, "@s", supersedesDecision); Add(cmd, "@t", IsoNow());
        cmd.ExecuteNonQuery();
        tx.Commit();
        return (true, null, eventId, decisionId);
    }

    private string currentRuleVersion(string branchId)
    {
        using var tx = Conn.BeginTransaction();
        var v = currentRuleVersion(tx);
        tx.Commit();
        return v;
    }

    /// <summary>撤销裁定：生成新的 UNDONE 事件并恢复前一分支状态，不删除历史。</summary>
    public (bool ok, string? conflictWith, string? eventId) UndoLastDecision(string branchId, string expectedHead)
    {
        string? lastDecisionId;
        using (var tx = Conn.BeginTransaction())
        {
            using var q = Conn.CreateCommand();
            q.Transaction = tx;
            q.CommandText = @"SELECT decision_id FROM decisions WHERE branch_id=@b ORDER BY created_at DESC, rowid DESC LIMIT 1;";
            Add(q, "@b", branchId);
            lastDecisionId = q.ExecuteScalar() as string;
            tx.Commit();
        }
        var deps = lastDecisionId is null ? new List<string>() : new List<string> { lastDecisionId };
        var (ok, conflict, ev) = AppendWithConcurrency(branchId, expectedHead, EventTypes.DecisionUndone,
            new { undoneDecision = lastDecisionId }, currentRuleVersion(branchId), deps, new());
        return (ok, conflict, ev);
    }

    public string SaveArtifact(string branchId, string eventId, string kind, object payload)
    {
        using var tx = Conn.BeginTransaction();
        string id = SaveArtifactTx(tx, branchId, eventId, kind, payload);
        tx.Commit();
        return id;
    }

    internal string SaveArtifactTx(Microsoft.Data.Sqlite.SqliteTransaction tx, string branchId, string eventId, string kind, object payload)
    {
        string id = "art." + Guid.NewGuid().ToString("N").Substring(0, 10);
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO artifacts(artifact_id,event_id,kind,payload_json,created_at)
                            VALUES(@id,@e,@k,@p,@t);";
        Add(cmd, "@id", id); Add(cmd, "@e", eventId); Add(cmd, "@k", kind);
        Add(cmd, "@p", JsonSerializer.Serialize(payload, payload.GetType(), JsonOpts)); Add(cmd, "@t", IsoNow());
        cmd.ExecuteNonQuery();
        return id;
    }
}

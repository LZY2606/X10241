using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using IsotopeMixing.Core.Domain;
using IsotopeMixing.Core.Engine;
using IsotopeMixing.Core.Identity;
using IsotopeMixing.Store.Serialization;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Store;

public sealed class RevisionConflictException : Exception
{
    public string ServerToken { get; }
    public string? TheirEventId { get; }

    public RevisionConflictException(string serverToken, string? theirEventId)
        : base("分支已被另一个窗口更新；提交基于过期版本，发生版本冲突。")
    {
        ServerToken = serverToken;
        TheirEventId = theirEventId;
    }
}

public sealed partial class IsotopeRepository
{
    // 依赖指纹：仅当真正引用的来源版本变化时，对应结果才失效（局部失效，不是全局替换）。
    public static string DependencyFingerprint(EngineRequest request) =>
        StableIds.V5(
            $"sample={request.Sample.Id}" +
            $"|em={request.EndmemberSet.Id}" +
            $"|frac={request.Fractionation.Id}" +
            $"|cov={request.Covariance?.Id ?? "cov:unit-fallback"}" +
            $"|prior={request.Prior.Id}" +
            $"|rule={MixingEngine.RuleHash}");

    // 保存一次分析（不修改任何原数据）。accepted=false 表示未采纳候选，同样保留。
    public string SaveAnalysisResult(string caseId, string branchId, AnalysisResult result, bool accepted, string actorEventId)
    {
        string resultId = StableIds.Scoped("result", caseId,
            $"{branchId}|{actorEventId}|{result.DependencyFingerprint()}");

        using var c = Open();
        using var cmd = new SqliteCommand(
            "INSERT OR IGNORE INTO analysis_results" +
            "(case_id, result_id, branch_id, status, inputs_json, regions_json, candidates_json," +
            "residuals_json, findings_json, measured_dimensions_json, missing_dimensions_json," +
            "rule_version, rule_hash, dependency_fingerprint, accepted, created_event_id, created_at)" +
            "VALUES ($case,$id,$branch,$status,$inputs,$regions,$cands,$res,$findings,$meas,$miss," +
            "$rv,$rh,$fp,$accepted,$event,$at);", c);
        AddResultParameters(cmd, caseId, resultId, branchId, result, accepted, actorEventId);
        cmd.ExecuteNonQuery();
        return resultId;
    }

    private static void AddResultParameters(SqliteCommand cmd, string caseId, string resultId,
        string branchId, AnalysisResult result, bool accepted, string eventId)
    {
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", resultId);
        cmd.Parameters.AddWithValue("$branch", branchId);
        cmd.Parameters.AddWithValue("$status", result.Status.ToString());
        cmd.Parameters.AddWithValue("$inputs", DomainJson.Serialize(result.Inputs));
        cmd.Parameters.AddWithValue("$regions", DomainJson.Serialize(result.Regions));
        cmd.Parameters.AddWithValue("$candidates", DomainJson.Serialize(result.AllCandidates));
        cmd.Parameters.AddWithValue("$res", DomainJson.Serialize(result.Residuals));
        cmd.Parameters.AddWithValue("$findings", DomainJson.Serialize(result.Findings));
        cmd.Parameters.AddWithValue("$meas", DomainJson.Serialize(result.MeasuredDimensions));
        cmd.Parameters.AddWithValue("$miss", DomainJson.Serialize(result.MissingDimensions));
        cmd.Parameters.AddWithValue("$rv", result.RuleVersion);
        cmd.Parameters.AddWithValue("$rh", result.RuleHash);
        cmd.Parameters.AddWithValue("$fp", result.DependencyFingerprint());
        cmd.Parameters.AddWithValue("$accepted", accepted ? 1L : 0L);
        cmd.Parameters.AddWithValue("$event", eventId);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
    }

    public StoredAnalysisResult GetAnalysisResult(string caseId, string resultId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT result_id, branch_id, status, inputs_json, regions_json, candidates_json," +
            "residuals_json, findings_json, measured_dimensions_json, missing_dimensions_json," +
            "rule_version, rule_hash, dependency_fingerprint, accepted, created_event_id" +
            "FROM analysis_results WHERE case_id=$case AND result_id=$id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", resultId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"结果 {resultId} 不存在。");
        return new StoredAnalysisResult(
            r.GetString(0), r.GetString(1),
            Enum.Parse<AnalysisStatus>(r.GetString(2)),
            DomainJson.Deserialize<AnalysisInputs>(r.GetString(3))!,
            r.GetString(4), r.GetString(5), r.GetString(6),
            r.GetString(7), r.GetString(8), r.GetString(9),
            r.GetString(10), r.GetString(11), r.GetString(12),
            r.GetInt64(13) != 0, r.GetString(14));
    }

    // 追加事件（analysis / accept / reject / adjust / undo）。
    // baseRevisionToken 与当前分支不一致时抛 RevisionConflictException（两个窗口后提交者看到冲突）。
    public DecisionEvent AppendEvent(string caseId, string branchId, string type, string actor,
        string payloadJson, string? baseRevisionToken,
        string? analysisResultId = null, string? undoesEventId = null)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        string currentToken;
        string? headEventId;
        using (var load = new SqliteCommand(
            "SELECT revision_token, head_event_id FROM branches WHERE case_id=$case AND branch_id=$id;", c, tx))
        {
            load.Parameters.AddWithValue("$case", caseId);
            load.Parameters.AddWithValue("$id", branchId);
            using var rr = load.ExecuteReader();
            if (!rr.Read()) throw new KeyNotFoundException($"分支 {branchId} 不存在。");
            currentToken = rr.GetString(0);
            headEventId = rr.IsDBNull(1) ? null : rr.GetString(1);
        }

        if (!string.Equals(currentToken, baseRevisionToken, StringComparison.Ordinal))
        {
            tx.Rollback();
            throw new RevisionConflictException(currentToken, headEventId);
        }

        int seq;
        using (var seqCmd = new SqliteCommand(
            "SELECT COALESCE(MAX(seq), 0) + 1 FROM events WHERE case_id=$case AND branch_id=$branch;", c, tx))
        {
            seqCmd.Parameters.AddWithValue("$case", caseId);
            seqCmd.Parameters.AddWithValue("$branch", branchId);
            seq = Convert.ToInt32(seqCmd.ExecuteScalar());
        }

        string eventId = StableIds.Scoped("event", caseId, $"{branchId}|{seq}|{type}");
        string newToken = StableIds.V5($"revision|{caseId}|{branchId}|{eventId}");

        using (var insert = new SqliteCommand(
            "INSERT INTO events" +
            "(case_id, branch_id, seq, event_id, type, analysis_result_id, supersedes_event_id," +
            "undoes_event_id, actor, payload_json, base_revision_token, new_revision_token, created_at)" +
            "VALUES ($case,$branch,$seq,$eid,$type,$result,$super,$undo,$actor,$payload,$base,$new,$at);", c, tx))
        {
            insert.Parameters.AddWithValue("$case", caseId);
            insert.Parameters.AddWithValue("$branch", branchId);
            insert.Parameters.AddWithValue("$seq", seq);
            insert.Parameters.AddWithValue("$eid", eventId);
            insert.Parameters.AddWithValue("$type", type);
            insert.Parameters.AddWithValue("$result", (object?)analysisResultId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$super", (object?)headEventId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$undo", (object?)undoesEventId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$actor", actor);
            insert.Parameters.AddWithValue("$payload", payloadJson);
            insert.Parameters.AddWithValue("$base", (object?)baseRevisionToken ?? DBNull.Value);
            insert.Parameters.AddWithValue("$new", newToken);
            insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        using (var update = new SqliteCommand(
            "UPDATE branches SET head_event_id=$head, revision_token=$token WHERE case_id=$case AND branch_id=$branch;", c, tx))
        {
            update.Parameters.AddWithValue("$head", eventId);
            update.Parameters.AddWithValue("$token", newToken);
            update.Parameters.AddWithValue("$case", caseId);
            update.Parameters.AddWithValue("$branch", branchId);
            update.ExecuteNonQuery();
        }

        // 接受/否定只更新该结果的裁定标记；不覆盖历史结果本身。
        if (analysisResultId is not null && (type == "accept" || type == "reject"))
        {
            using var mark = new SqliteCommand(
                "UPDATE analysis_results SET accepted=$acc WHERE case_id=$case AND result_id=$id;", c, tx);
            mark.Parameters.AddWithValue("$acc", type == "accept" ? 1L : 0L);
            mark.Parameters.AddWithValue("$case", caseId);
            mark.Parameters.AddWithValue("$id", analysisResultId);
            mark.ExecuteNonQuery();
        }

        tx.Commit();
        return new DecisionEvent(seq, eventId, branchId, type, analysisResultId,
            headEventId, undoesEventId, actor, payloadJson, baseRevisionToken, newToken, DateTimeOffset.UtcNow);
    }

    public List<DecisionEvent> ListEvents(string caseId, string branchId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT seq, event_id, branch_id, type, analysis_result_id, supersedes_event_id," +
            "undoes_event_id, actor, payload_json, base_revision_token, new_revision_token, created_at" +
            "FROM events WHERE case_id=$case AND branch_id=$branch ORDER BY seq;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$branch", branchId);
        var list = new List<DecisionEvent>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DecisionEvent(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetString(7), r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.GetString(10), DateTimeOffset.Parse(r.GetString(11))));
        }
        return list;
    }

    // 撤销：追加一个 undo 事件（旧事件不删除），并把分支头回退到被撤销事件之前，
    // 视图因此恢复前一分支状态；再次接受将在旧历史之上产生新事件。
    public DecisionEvent UndoLastDecision(string caseId, string branchId, string actor, string baseRevisionToken)
    {
        var events = ListEvents(caseId, branchId);
        var lastDecision = events.LastOrDefault(e => e.Type is "accept" or "reject" or "adjust");
        if (lastDecision is null)
            throw new InvalidOperationException("分支上没有可撤销的人工裁定。");

        var payload = new JsonObject
        {
            ["undoes"] = lastDecision.EventId,
            ["restoredHead"] = lastDecision.SupersedesEventId
        }.ToJsonString();

        var undoEvent = AppendEvent(caseId, branchId, "undo", actor, payload,
            baseRevisionToken, analysisResultId: null, undoesEventId: lastDecision.EventId);

        // 把头指针显式恢复到撤销目标之前（新事件仍保留在事件流里）。
        using var c = Open();
        using var update = new SqliteCommand(
            "UPDATE branches SET head_event_id=$head WHERE case_id=$case AND branch_id=$branch;", c);
        update.Parameters.AddWithValue("$head", (object?)lastDecision.SupersedesEventId ?? DBNull.Value);
        update.Parameters.AddWithValue("$case", caseId);
        update.Parameters.AddWithValue("$branch", branchId);
        update.ExecuteNonQuery();
        return undoEvent;
    }
}

public sealed record StoredAnalysisResult(
    string ResultId,
    string BranchId,
    AnalysisStatus Status,
    AnalysisInputs Inputs,
    string RegionsJson,
    string CandidatesJson,
    string ResidualsJson,
    string FindingsJson,
    string MeasuredDimensionsJson,
    string MissingDimensionsJson,
    string RuleVersion,
    string RuleHash,
    string DependencyFingerprint,
    bool Accepted,
    string CreatedEventId);

internal static class ResultExtensions
{
    public static string DependencyFingerprint(this AnalysisResult result) =>
        StableIds.V5(
            $"sample={result.Inputs.SampleId}" +
            $"|em={result.Inputs.EndmemberSetVersionId}" +
            $"|frac={result.Inputs.FractionationVersionId}" +
            $"|cov={result.Inputs.CovarianceId}" +
            $"|prior={result.Inputs.PriorId}" +
            $"|rule={result.RuleHash}");
}

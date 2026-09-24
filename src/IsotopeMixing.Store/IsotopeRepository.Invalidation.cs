using System;
using System.Collections.Generic;
using System.Linq;
using IsotopeMixing.Core.Identity;
using IsotopeMixing.Store.Serialization;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Store;

public sealed record InvalidationReport(
    string InvalidationId,
    string ChangedKind,
    string ChangedVersionId,
    IReadOnlyList<string> AffectedResultIds,
    IReadOnlyList<string> UnaffectedResultIds,
    string Reason);

public sealed partial class IsotopeRepository
{
    // 参数/参考版本变化后，用显式依赖计算失效范围：
    // 只失效真正引用该版本的结果，其它分支的旧结论原样保留，绝不全局替换。
    public InvalidationReport ComputeInvalidation(
        string caseId, string changedKind, string changedVersionId)
    {
        var (affected, unaffected) = ClassifyResults(caseId, changedKind, changedVersionId);
        string reason = changedKind switch
        {
            "endmember-set" => $"端元版本 {changedVersionId} 变化，仅引用该端元版本的结果失效。",
            "fractionation" => $"分馏版本 {changedVersionId} 变化，仅引用该分馏版本的结果失效。",
            "covariance" => $"协方差 {changedVersionId} 变化，仅引用该协方差的结果失效。",
            "prior" => $"先验 {changedVersionId} 变化，仅引用该先验的结果失效。",
            "sample" => $"样品 {changedVersionId} 变化，仅引用该样品的结果失效。",
            _ => $"规则/参考 {changedVersionId} 变化，按依赖关系限定失效范围。"
        };

        string invalidationId = StableIds.Scoped("invalidation", caseId,
            $"{changedKind}|{changedVersionId}");

        using var c = Open();
        using var cmd = new SqliteCommand(
            "INSERT OR REPLACE INTO invalidations" +
            "(case_id, invalidation_id, changed_kind, changed_version_id," +
            "affected_result_ids_json, unaffected_result_ids_json, reason, created_at)" +
            "VALUES ($case,$id,$kind,$ver,$affected,$unaffected,$reason,$at);", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", invalidationId);
        cmd.Parameters.AddWithValue("$kind", changedKind);
        cmd.Parameters.AddWithValue("$ver", changedVersionId);
        cmd.Parameters.AddWithValue("$affected", DomainJson.Serialize(affected));
        cmd.Parameters.AddWithValue("$unaffected", DomainJson.Serialize(unaffected));
        cmd.Parameters.AddWithValue("$reason", reason);
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        return new InvalidationReport(invalidationId, changedKind, changedVersionId,
            affected, unaffected, reason);
    }

    public List<InvalidationReport> ListInvalidations(string caseId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT invalidation_id, changed_kind, changed_version_id," +
            "affected_result_ids_json, unaffected_result_ids_json, reason" +
            "FROM invalidations WHERE case_id=$case ORDER BY created_at;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        var list = new List<InvalidationReport>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new InvalidationReport(
                r.GetString(0), r.GetString(1), r.GetString(2),
                DomainJson.Deserialize<List<string>>(r.GetString(3))!,
                DomainJson.Deserialize<List<string>>(r.GetString(4))!,
                r.GetString(5)));
        }
        return list;
    }

    private (List<string> Affected, List<string> Unaffected) ClassifyResults(
        string caseId, string changedKind, string changedVersionId)
    {
        var affected = new List<string>();
        var unaffected = new List<string>();

        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT result_id, inputs_json, rule_hash FROM analysis_results WHERE case_id=$case ORDER BY created_event_id, result_id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string resultId = r.GetString(0);
            var inputs = DomainJson.Deserialize<Core.Domain.AnalysisInputs>(r.GetString(1))!;
            string ruleHash = r.GetString(2);

            bool isAffected = changedKind switch
            {
                "endmember-set" => inputs.EndmemberSetVersionId == changedVersionId,
                "fractionation" => inputs.FractionationVersionId == changedVersionId,
                "covariance" => inputs.CovarianceId == changedVersionId,
                "prior" => inputs.PriorId == changedVersionId,
                "sample" => inputs.SampleId == changedVersionId,
                "rule" => ruleHash != changedVersionId,
                _ => false
            };

            (isAffected ? affected : unaffected).Add(resultId);
        }
        return (affected, unaffected);
    }
}

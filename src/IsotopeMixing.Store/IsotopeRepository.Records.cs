using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using IsotopeMixing.Core.Domain;
using IsotopeMixing.Store.Serialization;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Store;

public sealed partial class IsotopeRepository
{
    public SampleRecord GetSample(string caseId, string sampleId, string versionId = "v1")
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT sample_id, label, values_json, units_json, provenance" +
            "FROM samples WHERE case_id=$case AND sample_id=$id AND version_id=$ver;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", sampleId);
        cmd.Parameters.AddWithValue("$ver", versionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"样品 {sampleId}@{versionId} 不存在。");
        return new SampleRecord(
            r.GetString(0), r.GetString(1),
            DomainJson.Deserialize<List<MeasuredValue>>(r.GetString(2))!,
            DomainJson.UnitsFromJson(JsonNode.Parse(r.GetString(3))!.AsArray()),
            r.IsDBNull(4) ? null : r.GetString(4));
    }

    public EndmemberSetVersion GetEndmemberSet(string caseId, string versionId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT version_id, label, parent_version_id, endmembers_json, dimension_order_json, created_at, change_note" +
            "FROM endmember_sets WHERE case_id=$case AND version_id=$id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", versionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"端元版本 {versionId} 不存在。");
        return new EndmemberSetVersion(
            r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            DomainJson.EndmembersFromJson(JsonNode.Parse(r.GetString(3))!.AsArray()),
            DomainJson.StringsFromJson(JsonNode.Parse(r.GetString(4))!.AsArray()),
            DateTimeOffset.Parse(r.GetString(5)),
            r.GetString(6));
    }

    public List<string> ListEndmemberSetVersions(string caseId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT version_id FROM endmember_sets WHERE case_id=$case ORDER BY created_at, version_id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        return ReadStrings(cmd);
    }

    public FractionationVersion GetFractionation(string caseId, string versionId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT version_id, label, parent_version_id, factors_json, created_at, change_note" +
            "FROM fractionations WHERE case_id=$case AND version_id=$id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", versionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"分馏版本 {versionId} 不存在。");
        return new FractionationVersion(
            r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            DomainJson.FactorsFromJson(JsonNode.Parse(r.GetString(3))!.AsObject()),
            DateTimeOffset.Parse(r.GetString(4)),
            r.GetString(5));
    }

    public List<string> ListFractionationVersions(string caseId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT version_id FROM fractionations WHERE case_id=$case ORDER BY created_at, version_id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        return ReadStrings(cmd);
    }

    public MeasurementCovariance? GetCovariance(string caseId, string covarianceId)
    {
        if (covarianceId == "cov:unit-fallback") return null;
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT covariance_id, label, sample_id, dimension_order_json, matrix_json, is_correlated, raw_rows_json" +
            "FROM covariances WHERE case_id=$case AND covariance_id=$id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", covarianceId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new MeasurementCovariance(
            r.GetString(0), r.GetString(1), r.GetString(2),
            DomainJson.StringsFromJson(JsonNode.Parse(r.GetString(3))!.AsArray()),
            DomainJson.Deserialize<double[][]>(r.GetString(4))!,
            r.GetInt64(5) != 0,
            DomainJson.Deserialize<List<string>?>(r.GetString(6)));
    }

    public PriorAssumption GetPrior(string caseId, string priorId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT prior_id, label, kind, intervals_json, dirichlet_json, raw_representation" +
            "FROM priors WHERE case_id=$case AND prior_id=$id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", priorId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"先验 {priorId} 不存在。");
        var dirichlet = JsonNode.Parse(r.GetString(4))!.AsObject();
        return new PriorAssumption(
            r.GetString(0), r.GetString(1), r.GetString(2),
            DomainJson.IntervalsFromJson(JsonNode.Parse(r.GetString(3))!.AsObject()),
            dirichlet.Count == 0 ? null : dirichlet.ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>()),
            r.GetString(5));
    }

    public List<BranchRow> ListBranches(string caseId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT branch_id, label, parent_branch_id, sample_id, endmember_version_id," +
            "fractionation_version_id, covariance_id, prior_id, head_event_id, revision_token" +
            "FROM branches WHERE case_id=$case ORDER BY created_at, branch_id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        var list = new List<BranchRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BranchRow(
                r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8), r.GetString(9)));
        }
        return list;
    }

    public BranchRow GetBranch(string caseId, string branchId)
    {
        using var c = Open();
        using var cmd = new SqliteCommand(
            "SELECT branch_id, label, parent_branch_id, sample_id, endmember_version_id," +
            "fractionation_version_id, covariance_id, prior_id, head_event_id, revision_token" +
            "FROM branches WHERE case_id=$case AND branch_id=$id;", c);
        cmd.Parameters.AddWithValue("$case", caseId);
        cmd.Parameters.AddWithValue("$id", branchId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"分支 {branchId} 不存在。");
        return new BranchRow(
            r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8), r.GetString(9));
    }

    // 对同一份材料的重新解释：新建分支；旧分支及其引用的原始字节继续可读。
    public BranchRow ForkBranch(string caseId, string parentBranchId, string newBranchId, string label,
        string? endmemberSet = null, string? fractionation = null, string? covariance = null, string? prior = null)
    {
        var parent = GetBranch(caseId, parentBranchId);
        using var c = Open();
        using var tx = c.BeginTransaction();
        CreateBranchRow(c, tx, caseId, newBranchId, label, parentBranchId,
            parent.SampleId,
            endmemberSet ?? parent.EndmemberVersionId,
            fractionation ?? parent.FractionationVersionId,
            covariance ?? parent.CovarianceId,
            prior ?? parent.PriorId);
        tx.Commit();
        return GetBranch(caseId, newBranchId);
    }

    private static List<string> ReadStrings(SqliteCommand cmd)
    {
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}

public sealed record BranchRow(
    string BranchId,
    string Label,
    string? ParentBranchId,
    string SampleId,
    string EndmemberVersionId,
    string FractionationVersionId,
    string CovarianceId,
    string PriorId,
    string? HeadEventId,
    string RevisionToken);

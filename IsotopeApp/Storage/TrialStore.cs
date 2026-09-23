using System.Text.Json;
using IsotopeApp.Domain;
using Microsoft.Data.Sqlite;

namespace IsotopeApp.Storage;

public sealed record StoredTrial(
    string TrialId, int TrialVersion, string BranchId, string SampleId, string SampleVersion,
    string FractionationId, string FractionationVersion, string? PriorId, string? PriorVersion,
    IReadOnlyList<DependencyRef> SourceVersions, string? CovarianceVersion,
    JsonDocument Result, string CreatedEventId, string CreatedAt, string Status);

public sealed partial class Store
{
    public StoredTrial SaveTrial(StoredTrial t)
    {
        using var tx = C.BeginTransaction();
        int version;
        using (var q = C.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT COALESCE(MAX(trial_version),0)+1 FROM trials WHERE trial_id=$id";
            q.Parameters.AddWithValue("$id", t.TrialId);
            version = Convert.ToInt32(q.ExecuteScalar());
        }
        using var cmd = C.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO trials(trial_id,trial_version,branch_id,sample_id,sample_version,fractionation_id,
                            fractionation_version,prior_id,prior_version,source_versions_json,covariance_version,result_json,
                            created_event_id,created_at,status)
                            VALUES($id,$v,$b,$sid,$sv,$fid,$fv,$pid,$pv,$src,$cv,$res,$ev,$at,$st)";
        cmd.Parameters.AddWithValue("$id", t.TrialId);
        cmd.Parameters.AddWithValue("$v", version);
        cmd.Parameters.AddWithValue("$b", t.BranchId);
        cmd.Parameters.AddWithValue("$sid", t.SampleId);
        cmd.Parameters.AddWithValue("$sv", t.SampleVersion);
        cmd.Parameters.AddWithValue("$fid", t.FractionationId);
        cmd.Parameters.AddWithValue("$fv", t.FractionationVersion);
        cmd.Parameters.AddWithValue("$pid", (object?)t.PriorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pv", (object?)t.PriorVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$src", Hashing.Canonical(t.SourceVersions));
        cmd.Parameters.AddWithValue("$cv", (object?)t.CovarianceVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$res", t.Result.RootElement.GetRawText());
        cmd.Parameters.AddWithValue("$ev", t.CreatedEventId);
        cmd.Parameters.AddWithValue("$at", t.CreatedAt);
        cmd.Parameters.AddWithValue("$st", t.Status);
        cmd.ExecuteNonQuery();
        tx.Commit();
        return t with { TrialVersion = version };
    }

    public StoredTrial? GetTrialAt(string branchId, string trialId, int? upToSeq = null)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = @"SELECT trial_version,sample_id,sample_version,fractionation_id,fractionation_version,
                            prior_id,prior_version,source_versions_json,covariance_version,result_json,created_event_id,created_at,status
                            FROM trials WHERE trial_id=$id AND branch_id=$b
                            AND ($seq IS NULL OR trial_version <= $seq)
                            ORDER BY trial_version DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$id", trialId);
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$seq", (object?)upToSeq ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new StoredTrial(
            trialId, r.GetInt32(0), branchId, r.GetString(1), r.GetString(2),
            r.GetString(3), r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
            JsonSerializer.Deserialize<List<DependencyRef>>(r.GetString(7), JsonOpts.Default) ?? new(),
            r.IsDBNull(8) ? null : r.GetString(8),
            JsonDocument.Parse(r.GetString(9)), r.GetString(10), r.GetString(11), r.GetString(12));
    }

    public IReadOnlyList<StoredTrial> LatestTrials(string branchId)
    {
        var list = new List<StoredTrial>();
        using var cmd = C.CreateCommand();
        cmd.CommandText = @"SELECT t.trial_id,MAX(t.trial_version) FROM trials t WHERE t.branch_id=$b GROUP BY t.trial_id";
        cmd.Parameters.AddWithValue("$b", branchId);
        using var r = cmd.ExecuteReader();
        var ids = new List<string>();
        while (r.Read()) ids.Add(r.GetString(0));
        r.Close();
        foreach (var id in ids)
        {
            var t = GetTrialAt(branchId, id);
            if (t is not null) list.Add(t);
        }
        return list;
    }

    // ---- version lookups for active interpretations ----
    public (string Id, string Version, string Json) ActiveSample(string branchId, string sampleId)
        => ActiveRow("samples", "sample_id", branchId, sampleId, "raw_json");

    public (string Id, string Version, string Json) ActiveSource(string branchId, string sourceId)
        => ActiveRow("sources", "source_id", branchId, sourceId, "modes_json");

    public (string Id, string Version, string Json)? CovarianceForSample(string branchId, string sampleId)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT covariance_id,version_hash,matrix_json,tracers_json,raw_token FROM covariances WHERE branch_id=$b AND sample_id=$s AND active=1 ORDER BY rowid DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$s", sampleId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var payload = JsonSerializer.Serialize(new { matrix = r.GetString(2), tracers = r.GetString(3), raw = r.GetString(4) });
        return (r.GetString(0), r.GetString(1), payload);
    }

    public (string Id, string Version, string Json) ActiveFractionation(string branchId, string fid)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT fractionation_id,version_hash,shifts_json,basis,label,raw_token FROM fractionations WHERE branch_id=$b AND fractionation_id=$id AND active=1 ORDER BY rowid DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$id", fid);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException(fid);
        var payload = JsonSerializer.Serialize(new { shifts = r.GetString(2), basis = r.GetString(3), label = r.GetString(4), raw = r.GetString(5) });
        return (r.GetString(0), r.GetString(1), payload);
    }

    public (string Id, string Version, string Json)? ActivePrior(string branchId, string pid)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = "SELECT prior_id,version_hash,bounds_json,kind,raw_token FROM priors WHERE branch_id=$b AND prior_id=$id AND active=1 ORDER BY rowid DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$id", pid);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var payload = JsonSerializer.Serialize(new { bounds = r.GetString(2), kind = r.GetString(3), raw = r.GetString(4) });
        return (r.GetString(0), r.GetString(1), payload);
    }

    public IReadOnlyList<(string Id, string Version, string Json)> ActiveSources(string branchId)
    {
        var list = new List<(string, string, string)>();
        using var cmd = C.CreateCommand();
        cmd.CommandText = @"SELECT source_id,version_hash,modes_json,name,means_raw,covariance_raw FROM sources s
                            WHERE branch_id=$b AND active=1 AND rowid=(SELECT MAX(rowid) FROM sources WHERE source_id=s.source_id AND branch_id=$b)";
        cmd.Parameters.AddWithValue("$b", branchId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetString(1),
                      JsonSerializer.Serialize(new { modes = r.GetString(2), name = r.GetString(3), meansRaw = r.GetString(4), covRaw = r.GetString(5) })));
        return list;
    }

    private (string, string, string) ActiveRow(string table, string idCol, string branch, string id, string jsonCol)
    {
        using var cmd = C.CreateCommand();
        cmd.CommandText = $"SELECT {idCol},version_hash,{jsonCol},name FROM {table} WHERE branch_id=$b AND {idCol}=$id AND active=1 ORDER BY rowid DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$b", branch);
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException($"{table}/{id}");
        return (r.GetString(0), r.GetString(1), JsonSerializer.Serialize(new { data = r.GetString(2), name = r.GetString(3) }));
    }
}

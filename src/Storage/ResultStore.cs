using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed class ResultDependency
{
    public required string ResultId;
    public required string DependencyUid;
    public required string DependencyKind;
    public required string DependencyFingerprint;
}

public sealed class ResultStore
{
    private readonly Database _db;
    public ResultStore(Database db) { _db = db; }

    public void Insert(ResultRow r, IEnumerable<(string uid, string kind, string fingerprint)> dependencies)
    {
        using var cn = _db.Open();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO results(result_id,branch_id,created_event_id,created_seq,scenario_id,scenario_label,
                sample_uid,endmember_set_id,cov_uid,frac_version_id,prior_id,unit,datum,status,identifiability,feasible,
                dof,chi2,p_value,best_weights_json,residual_json,region_json,assessments_json,inputs_json)
                VALUES($rid,$b,$e,$s,$scid,$scl,$su,$es,$cu,$fv,$pr,$u,$d,$st,$id,$fe,$dof,$chi,$pv,$bw,$rs,$rg,$as,$inp);";
            Bind(cmd, r);
            cmd.ExecuteNonQuery();
        }
        foreach (var dep in dependencies)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT OR IGNORE INTO result_dependencies(result_id,dependency_uid,dependency_kind,dependency_fingerprint)
                                VALUES($r,$u,$k,$f);";
            cmd.Parameters.AddWithValue("$r", r.ResultId);
            cmd.Parameters.AddWithValue("$u", dep.uid);
            cmd.Parameters.AddWithValue("$k", dep.kind);
            cmd.Parameters.AddWithValue("$f", dep.fingerprint);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static void Bind(SqliteCommand cmd, ResultRow r)
    {
        cmd.Parameters.AddWithValue("$rid", r.ResultId);
        cmd.Parameters.AddWithValue("$b", r.BranchId);
        cmd.Parameters.AddWithValue("$e", r.CreatedEventId);
        cmd.Parameters.AddWithValue("$s", r.CreatedSeq);
        cmd.Parameters.AddWithValue("$scid", r.ScenarioId);
        cmd.Parameters.AddWithValue("$scl", r.ScenarioLabel);
        cmd.Parameters.AddWithValue("$su", (object?)r.SampleUid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$es", r.EndmemberSetId);
        cmd.Parameters.AddWithValue("$cu", (object?)r.CovUid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fv", (object?)r.FracVersionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pr", (object?)r.PriorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$u", (object?)r.Unit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", (object?)r.Datum ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$st", r.Status);
        cmd.Parameters.AddWithValue("$id", r.Identifiability);
        cmd.Parameters.AddWithValue("$fe", r.Feasible ? 1 : 0);
        cmd.Parameters.AddWithValue("$dof", (object?)r.Dof ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$chi", (object?)r.Chi2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pv", (object?)r.PValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bw", (object?)r.BestWeightsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rs", (object?)r.ResidualJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rg", (object?)r.RegionJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$as", r.AssessmentsJson);
        cmd.Parameters.AddWithValue("$inp", r.InputsJson);
    }

    public List<ResultRow> ForBranch(string branchId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM results WHERE branch_id=$b ORDER BY created_seq, result_id;";
        cmd.Parameters.AddWithValue("$b", branchId);
        return Read(cmd);
    }

    public ResultRow? Get(string resultId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM results WHERE result_id=$r;";
        cmd.Parameters.AddWithValue("$r", resultId);
        return Read(cmd).FirstOrDefault();
    }

    /// <summary>Mark results invalidated whose dependency fingerprint changed. Returns affected result ids with scopes.</summary>
    public List<(string resultId, string scenarioId)> InvalidateByDependency(IEnumerable<string> changedUids, string eventId)
    {
        var affected = new List<(string, string)>();
        using var cn = _db.Open();
        using var tx = cn.BeginTransaction();
        foreach (var uid in changedUids)
        {
            using var find = cn.CreateCommand();
            find.Transaction = tx;
            find.CommandText = @"SELECT DISTINCT r.result_id, r.scenario_id
                                 FROM result_dependencies d JOIN results r ON r.result_id=d.result_id
                                 WHERE d.dependency_uid=$u AND r.invalidated=0;";
            find.Parameters.AddWithValue("$u", uid);
            using var rd = find.ExecuteReader();
            var rows = new List<(string, string)>();
            while (rd.Read()) rows.Add((rd.GetString(0), rd.GetString(1)));
            rd.Close();
            foreach (var (rid, scid) in rows)
            {
                using var up = cn.CreateCommand();
                up.Transaction = tx;
                up.CommandText = @"UPDATE results SET invalidated=1, invalidated_event_id=$e,
                                    invalidation_scope=COALESCE(invalidation_scope,'')||$scope
                                  WHERE result_id=$r AND invalidated=0;";
                up.Parameters.AddWithValue("$e", eventId);
                up.Parameters.AddWithValue("$scope", $"dependency:{uid};");
                up.Parameters.AddWithValue("$r", rid);
                up.ExecuteNonQuery();
                affected.Add((rid, scid));
            }
        }
        tx.Commit();
        return affected;
    }

    public List<ResultDependency> Dependencies(string resultId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT result_id,dependency_uid,dependency_kind,dependency_fingerprint FROM result_dependencies WHERE result_id=$r;";
        cmd.Parameters.AddWithValue("$r", resultId);
        using var rd = cmd.ExecuteReader();
        var list = new List<ResultDependency>();
        while (rd.Read())
            list.Add(new ResultDependency { ResultId = rd.GetString(0), DependencyUid = rd.GetString(1), DependencyKind = rd.GetString(2), DependencyFingerprint = rd.GetString(3) });
        return list;
    }

    private static List<ResultRow> Read(SqliteCommand cmd)
    {
        using var rd = cmd.ExecuteReader();
        var list = new List<ResultRow>();
        while (rd.Read())
        {
            string? N(int i) => rd.IsDBNull(i) ? null : rd.GetString(i);
            list.Add(new ResultRow
            {
                ResultId = rd.GetString(rd.GetOrdinal("result_id")),
                BranchId = rd.GetString(rd.GetOrdinal("branch_id")),
                CreatedEventId = rd.GetString(rd.GetOrdinal("created_event_id")),
                CreatedSeq = rd.GetInt64(rd.GetOrdinal("created_seq")),
                ScenarioId = rd.GetString(rd.GetOrdinal("scenario_id")),
                ScenarioLabel = rd.GetString(rd.GetOrdinal("scenario_label")),
                SampleUid = N(rd.GetOrdinal("sample_uid")),
                EndmemberSetId = rd.GetString(rd.GetOrdinal("endmember_set_id")),
                CovUid = N(rd.GetOrdinal("cov_uid")),
                FracVersionId = N(rd.GetOrdinal("frac_version_id")),
                PriorId = N(rd.GetOrdinal("prior_id")),
                Unit = N(rd.GetOrdinal("unit")),
                Datum = N(rd.GetOrdinal("datum")),
                Status = rd.GetString(rd.GetOrdinal("status")),
                Identifiability = rd.GetString(rd.GetOrdinal("identifiability")),
                Feasible = rd.GetInt32(rd.GetOrdinal("feasible")) == 1,
                Dof = rd.IsDBNull(rd.GetOrdinal("dof")) ? null : rd.GetInt32(rd.GetOrdinal("dof")),
                Chi2 = rd.IsDBNull(rd.GetOrdinal("chi2")) ? null : rd.GetDouble(rd.GetOrdinal("chi2")),
                PValue = rd.IsDBNull(rd.GetOrdinal("p_value")) ? null : rd.GetDouble(rd.GetOrdinal("p_value")),
                BestWeightsJson = N(rd.GetOrdinal("best_weights_json")),
                ResidualJson = N(rd.GetOrdinal("residual_json")),
                RegionJson = N(rd.GetOrdinal("region_json")),
                AssessmentsJson = rd.GetString(rd.GetOrdinal("assessments_json")),
                InputsJson = rd.GetString(rd.GetOrdinal("inputs_json")),
                Invalidated = rd.GetInt32(rd.GetOrdinal("invalidated")) == 1,
                InvalidatedEventId = N(rd.GetOrdinal("invalidated_event_id")),
                InvalidationScope = N(rd.GetOrdinal("invalidation_scope"))
            });
        }
        return list;
    }
}

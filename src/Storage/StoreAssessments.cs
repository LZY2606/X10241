using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace IsotopeMix.Storage;

public sealed record StoredAssessment(string AssessmentId, string BranchId, string SampleId,
    string SourceSetId, string FractionationId, string PriorId, string EngineVersion,
    JsonObject Result, string CreatedEventId);

public sealed record StoredDecision(string DecisionId, string BranchId, string EventId,
    string AssessmentId, string Verdict, string Note);

public sealed partial class Store
{
    public void PutAssessment(SqliteConnection conn, SqliteTransaction tx, StoredAssessment a)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO assessment
            (assessment_id,branch_id,sample_id,source_set_id,fractionation_id,prior_id,
             engine_version,result_json,created_event_id)
            VALUES($id,$b,$s,$ss,$f,$p,$ev,$json,$ce)
            ON CONFLICT(branch_id,assessment_id) DO UPDATE SET
              sample_id=excluded.sample_id, source_set_id=excluded.source_set_id,
              fractionation_id=excluded.fractionation_id, prior_id=excluded.prior_id,
              engine_version=excluded.engine_version, result_json=excluded.result_json,
              created_event_id=excluded.created_event_id";
        cmd.Parameters.AddWithValue("$id", a.AssessmentId);
        cmd.Parameters.AddWithValue("$b", a.BranchId);
        cmd.Parameters.AddWithValue("$s", a.SampleId);
        cmd.Parameters.AddWithValue("$ss", a.SourceSetId);
        cmd.Parameters.AddWithValue("$f", a.FractionationId);
        cmd.Parameters.AddWithValue("$p", a.PriorId);
        cmd.Parameters.AddWithValue("$ev", a.EngineVersion);
        cmd.Parameters.AddWithValue("$json", JsonIo.Canonical(a.Result));
        cmd.Parameters.AddWithValue("$ce", a.CreatedEventId);
        cmd.ExecuteNonQuery();
    }

    public StoredAssessment? GetAssessment(string branchId, string assessmentId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT assessment_id,branch_id,sample_id,source_set_id,fractionation_id,
            prior_id,engine_version,result_json,created_event_id
            FROM assessment WHERE branch_id=$b AND assessment_id=$a";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$a", assessmentId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new StoredAssessment(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.GetString(4), r.GetString(5), r.GetString(6), JsonIo.ParseObject(r.GetString(7)),
            r.GetString(8));
    }

    public List<StoredAssessment> ListAssessments(string branchId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT assessment_id,branch_id,sample_id,source_set_id,fractionation_id,
            prior_id,engine_version,result_json,created_event_id
            FROM assessment WHERE branch_id=$b ORDER BY assessment_id";
        cmd.Parameters.AddWithValue("$b", branchId);
        var list = new List<StoredAssessment>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StoredAssessment(r.GetString(0), r.GetString(1), r.GetString(2),
                r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                JsonIo.ParseObject(r.GetString(7)), r.GetString(8)));
        return list;
    }

    public void PutDecision(SqliteConnection conn, SqliteTransaction tx, StoredDecision d)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO decision(decision_id,branch_id,event_id,assessment_id,verdict,note)
            VALUES($id,$b,$e,$a,$v,$n)";
        cmd.Parameters.AddWithValue("$id", d.DecisionId);
        cmd.Parameters.AddWithValue("$b", d.BranchId);
        cmd.Parameters.AddWithValue("$e", d.EventId);
        cmd.Parameters.AddWithValue("$a", d.AssessmentId);
        cmd.Parameters.AddWithValue("$v", d.Verdict);
        cmd.Parameters.AddWithValue("$n", d.Note ?? "");
        cmd.ExecuteNonQuery();
    }

    public List<StoredDecision> ListDecisions(string branchId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT decision_id,branch_id,event_id,assessment_id,verdict,note FROM decision WHERE branch_id=$b ORDER BY decision_id";
        cmd.Parameters.AddWithValue("$b", branchId);
        var list = new List<StoredDecision>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StoredDecision(r.GetString(0), r.GetString(1), r.GetString(2),
                r.GetString(3), r.GetString(4), r.GetString(5)));
        return list;
    }

    public void SetMeta(SqliteConnection conn, SqliteTransaction? tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string? GetMeta(string key)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public Dictionary<string, string> AllMeta()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key,value FROM meta ORDER BY key";
        var dict = new Dictionary<string, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) dict[r.GetString(0)] = r.GetString(1);
        return dict;
    }
}

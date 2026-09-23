using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed class DecisionRow
{
    public required string DecisionId;
    public required string BranchId;
    public required string EventId;
    public required long Seq;
    public required string ScenarioId;
    public string? ResultId;
    public required string Verdict;
    public required string Rationale;
    public required string At;
    public bool Revoked;
    public string? RevokeEventId;
}

public sealed class DecisionStore
{
    private readonly Database _db;
    public DecisionStore(Database db) { _db = db; }

    public void Add(DecisionRow d)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"INSERT INTO decisions(decision_id,branch_id,event_id,seq,scenario_id,result_id,verdict,rationale,at)
                            VALUES($id,$b,$e,$s,$sc,$r,$v,$ra,$t);";
        cmd.Parameters.AddWithValue("$id", d.DecisionId);
        cmd.Parameters.AddWithValue("$b", d.BranchId);
        cmd.Parameters.AddWithValue("$e", d.EventId);
        cmd.Parameters.AddWithValue("$s", d.Seq);
        cmd.Parameters.AddWithValue("$sc", d.ScenarioId);
        cmd.Parameters.AddWithValue("$r", (object?)d.ResultId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$v", d.Verdict);
        cmd.Parameters.AddWithValue("$ra", d.Rationale);
        cmd.Parameters.AddWithValue("$t", d.At);
        cmd.ExecuteNonQuery();
    }

    public DecisionRow? Get(string decisionId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM decisions WHERE decision_id=$id;";
        cmd.Parameters.AddWithValue("$id", decisionId);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new DecisionRow
        {
            DecisionId = rd.GetString(rd.GetOrdinal("decision_id")),
            BranchId = rd.GetString(rd.GetOrdinal("branch_id")),
            EventId = rd.GetString(rd.GetOrdinal("event_id")),
            Seq = rd.GetInt64(rd.GetOrdinal("seq")),
            ScenarioId = rd.GetString(rd.GetOrdinal("scenario_id")),
            ResultId = rd.IsDBNull(rd.GetOrdinal("result_id")) ? null : rd.GetString(rd.GetOrdinal("result_id")),
            Verdict = rd.GetString(rd.GetOrdinal("verdict")),
            Rationale = rd.GetString(rd.GetOrdinal("rationale")),
            At = rd.GetString(rd.GetOrdinal("at")),
            Revoked = rd.GetInt32(rd.GetOrdinal("revoked")) == 1,
            RevokeEventId = rd.IsDBNull(rd.GetOrdinal("revoke_event_id")) ? null : rd.GetString(rd.GetOrdinal("revoke_event_id"))
        };
    }

    public void Revoke(string decisionId, string revokeEventId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE decisions SET revoked=1, revoke_event_id=$e WHERE decision_id=$id;";
        cmd.Parameters.AddWithValue("$e", revokeEventId);
        cmd.Parameters.AddWithValue("$id", decisionId);
        cmd.ExecuteNonQuery();
    }

    public List<DecisionRow> ForBranch(string branchId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT * FROM decisions WHERE branch_id=$b ORDER BY seq;";
        cmd.Parameters.AddWithValue("$b", branchId);
        using var rd = cmd.ExecuteReader();
        var list = new List<DecisionRow>();
        while (rd.Read())
            list.Add(new DecisionRow
            {
                DecisionId = rd.GetString(0), BranchId = rd.GetString(1), EventId = rd.GetString(2),
                Seq = rd.GetInt64(3), ScenarioId = rd.GetString(4),
                ResultId = rd.IsDBNull(5) ? null : rd.GetString(5),
                Verdict = rd.GetString(6), Rationale = rd.GetString(7), At = rd.GetString(8),
                Revoked = rd.GetInt32(9) == 1, RevokeEventId = rd.IsDBNull(10) ? null : rd.GetString(10)
            });
        return list;
    }
}

public sealed class ArchiveStore
{
    private readonly Database _db;
    public ArchiveStore(Database db) { _db = db; }

    public void Add(string archiveId, string branchId, string label, string at, string baselineEventId,
        string? changedSourceUid, string? changeKind, string? acceptedResultId, string? rejectedResultId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"INSERT INTO archive_runs(archive_id,branch_id,label,at,baseline_event_id,
            changed_source_uid,change_kind,accepted_result_id,rejected_result_id)
            VALUES($a,$b,$l,$t,$be,$u,$k,$ac,$rj);";
        cmd.Parameters.AddWithValue("$a", archiveId);
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$l", label);
        cmd.Parameters.AddWithValue("$t", at);
        cmd.Parameters.AddWithValue("$be", baselineEventId);
        cmd.Parameters.AddWithValue("$u", (object?)changedSourceUid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$k", (object?)changeKind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ac", (object?)acceptedResultId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rj", (object?)rejectedResultId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}

using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace IsotopeMixing.Storage;

public sealed class BranchInfo
{
    public required string BranchId;
    public string? ParentBranchId;
    public required string Name;
    public required string CreatedAt;
    public required string Note;
    public string? BaselineBatchId;
}

public sealed class BranchStore
{
    private readonly Database _db;
    public BranchStore(Database db) { _db = db; }

    public void CreateBranch(string id, string? parent, string name, string note, string? baselineBatchId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"INSERT INTO branches(branch_id,parent_branch_id,name,created_at,note,baseline_batch_id)
                            VALUES($id,$p,$n,$t,$note,$b);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$p", (object?)parent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$note", note);
        cmd.Parameters.AddWithValue("$b", (object?)baselineBatchId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<BranchInfo> ListBranches()
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT branch_id,parent_branch_id,name,created_at,note,baseline_batch_id FROM branches ORDER BY created_at;";
        using var rd = cmd.ExecuteReader();
        var list = new List<BranchInfo>();
        while (rd.Read())
            list.Add(new BranchInfo
            {
                BranchId = rd.GetString(0),
                ParentBranchId = rd.IsDBNull(1) ? null : rd.GetString(1),
                Name = rd.GetString(2), CreatedAt = rd.GetString(3), Note = rd.GetString(4),
                BaselineBatchId = rd.IsDBNull(5) ? null : rd.GetString(5)
            });
        return list;
    }

    public long NextSeq(string branchId, SqliteConnection cn, SqliteTransaction tx)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(seq),-1)+1 FROM events WHERE branch_id=$b;";
        cmd.Parameters.AddWithValue("$b", branchId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Append an event with optimistic expectedSeq. Throws ConflictException on mismatch.</summary>
    public (long seq, string eventId) AppendEvent(string branchId, long? expectedSeq, string type, JsonNode payload,
        string? producedResultId = null, string? undoOfEventId = null, string? eventId = null)
    {
        using var cn = _db.Open();
        using var tx = cn.BeginTransaction();
        long seq;
        using (var lockCmd = cn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT seq FROM events WHERE branch_id=$b ORDER BY seq DESC LIMIT 1;";
            lockCmd.Parameters.AddWithValue("$b", branchId);
            var cur = lockCmd.ExecuteScalar();
            long head = cur is null ? -1 : Convert.ToInt64(cur);
            seq = head + 1;
            if (expectedSeq is not null && expectedSeq.Value != seq)
            {
                tx.Rollback();
                throw new ConflictException(branchId, expectedSeq.Value, seq);
            }
        }
        string eid = eventId ?? DeterministicIds.NewEventId(branchId, seq, type);
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO events(branch_id,seq,event_id,at,event_type,payload_json,produced_result_id,undo_of_event_id)
                                VALUES($b,$s,$id,$t,$ty,$p,$r,$u);";
            cmd.Parameters.AddWithValue("$b", branchId);
            cmd.Parameters.AddWithValue("$s", seq);
            cmd.Parameters.AddWithValue("$id", eid);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$ty", type);
            cmd.Parameters.AddWithValue("$p", payload.ToJsonString());
            cmd.Parameters.AddWithValue("$r", (object?)producedResultId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$u", (object?)undoOfEventId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return (seq, eid);
    }

    public List<EventRow> Events(string branchId, long? upToSeq = null)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT branch_id,seq,event_id,at,event_type,payload_json,produced_result_id,undo_of_event_id FROM events WHERE branch_id=$b"
            + (upToSeq is null ? "" : " AND seq<=$u") + " ORDER BY seq;";
        cmd.Parameters.AddWithValue("$b", branchId);
        if (upToSeq is not null) cmd.Parameters.AddWithValue("$u", upToSeq.Value);
        using var rd = cmd.ExecuteReader();
        var list = new List<EventRow>();
        while (rd.Read())
            list.Add(new EventRow
            {
                BranchId = rd.GetString(0), Seq = rd.GetInt64(1), EventId = rd.GetString(2),
                At = rd.GetString(3), EventType = rd.GetString(4),
                Payload = JsonNode.Parse(rd.GetString(5))!,
                ProducedResultId = rd.IsDBNull(6) ? null : rd.GetString(6),
                UndoOfEventId = rd.IsDBNull(7) ? null : rd.GetString(7)
            });
        return list;
    }

    public long HeadSeq(string branchId)
    {
        using var cn = _db.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(seq),-1) FROM events WHERE branch_id=$b;";
        cmd.Parameters.AddWithValue("$b", branchId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}

public sealed class ConflictException : Exception
{
    public string BranchId { get; }
    public long ExpectedNextSeq { get; }
    public long ActualNextSeq { get; }
    public ConflictException(string branchId, long expected, long actual)
        : base($"版本冲突：分支 {branchId} 期望下一个序号 {expected}，实际 {actual}（另一窗口已先提交）")
    { BranchId = branchId; ExpectedNextSeq = expected; ActualNextSeq = actual; }
}

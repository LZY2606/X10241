using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed partial class Store
{
    public int LatestVersionNo(SqliteTransaction tx, string stableId)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(version_no),0) FROM raw_versions WHERE stable_id=@id;";
        Add(cmd, "@id", stableId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int LatestVersionNo(string stableId)
    {
        using var tx = Conn.BeginTransaction();
        int v = LatestVersionNo(tx, stableId);
        tx.Commit();
        return v;
    }

    private string CreateBranchRow(SqliteTransaction tx, string branchId, string label, string? parentBranch, string? parentEvent, string basis)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO branches(branch_id,label,parent_branch,parent_event,basis,head_event,created_at)
                            VALUES(@b,@l,@pb,@pe,@basis,NULL,@t);";
        Add(cmd, "@b", branchId); Add(cmd, "@l", label); Add(cmd, "@pb", parentBranch);
        Add(cmd, "@pe", parentEvent); Add(cmd, "@basis", basis); Add(cmd, "@t", IsoNow());
        cmd.ExecuteNonQuery();
        return branchId;
    }

    /// <summary>对同一材料的重新解释必须新建分支；旧分支、裁定与其引用的原始字节继续可读。</summary>
    public string CreateBranch(string label, string parentBranch, string basis)
    {
        using var tx = Conn.BeginTransaction();
        string parentHead = GetHead(parentBranch, tx) ?? throw new InvalidOperationException("父分支不存在。");
        string branchId = "branch." + Guid.NewGuid().ToString("N").Substring(0, 12);
        CreateBranchRow(tx, branchId, label, parentBranch, parentHead, basis);
        string ev = AppendEvent(tx, branchId, EventTypes.BranchCreated,
            new { label, parentBranch, forkedFrom = parentHead, basis },
            currentRuleVersion(tx), depsArray: new List<string> { parentBranch }, sourceBlobs: new());
        SetBranchHead(tx, branchId, ev);
        tx.Commit();
        return branchId;
    }

    public string? GetHead(string branchId)
    {
        using var tx = Conn.BeginTransaction();
        var h = GetHead(branchId, tx);
        tx.Commit();
        return h;
    }

    internal string? GetHead(string branchId, SqliteTransaction tx)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT head_event FROM branches WHERE branch_id=@b;";
        Add(cmd, "@b", branchId);
        return cmd.ExecuteScalar() as string;
    }

    private void SetBranchHead(SqliteTransaction tx, string branchId, string eventId)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE branches SET head_event=@h WHERE branch_id=@b;";
        Add(cmd, "@h", eventId); Add(cmd, "@b", branchId);
        cmd.ExecuteNonQuery();
    }

    private int NextSeq(SqliteTransaction tx, string branchId)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(seq),0)+1 FROM events WHERE branch_id=@b;";
        Add(cmd, "@b", branchId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private string currentRuleVersion(SqliteTransaction tx)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM meta WHERE key='rule_version';";
        return cmd.ExecuteScalar() as string ?? "mixing-rules/v1.0";
    }

    /// <summary>仅追加事件。atEvent=该分支基于的前一事件；乐观冲突由调用方比对 head。</summary>
    public string AppendEvent(SqliteTransaction tx, string branchId, string type, object payload,
        string ruleVersion,
        List<string>? depsArray = null,
        List<string>? sourceBlobs = null,
        string? atEvent = null)
    {
        string? expectedParent = atEvent ?? GetHead(branchId, tx);
        string eventId = "evt." + Guid.NewGuid().ToString("N").Substring(0, 12);
        int seq = NextSeq(tx, branchId);
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO events(event_id,seq,branch_id,at_event,type,payload_json,rule_version,deps_json,source_blobs_json,created_at)
                            VALUES(@id,@seq,@b,@at,@type,@payload,@rule,@deps,@blobs,@t);";
        Add(cmd, "@id", eventId); Add(cmd, "@seq", seq); Add(cmd, "@b", branchId);
        Add(cmd, "@at", expectedParent); Add(cmd, "@type", type);
        Add(cmd, "@payload", JsonSerializer.Serialize(payload, JsonOpts));
        Add(cmd, "@rule", ruleVersion);
        Add(cmd, "@deps", JsonSerializer.Serialize(depsArray ?? new List<string>(), JsonOpts));
        Add(cmd, "@blobs", JsonSerializer.Serialize(sourceBlobs ?? new List<string>(), JsonOpts));
        Add(cmd, "@t", IsoNow());
        cmd.ExecuteNonQuery();
        SetBranchHead(tx, branchId, eventId);
        return eventId;
    }

    /// <summary>乐观提交：expectedHead 与当前 head 不一致时返回 409 风格冲突，不写入。</summary>
    public (bool ok, string? conflictWith, string? eventId) AppendWithConcurrency(
        string branchId, string expectedHead, string type, object payload,
        string ruleVersion, List<string>? deps = null, List<string>? blobs = null)
    {
        using var tx = Conn.BeginTransaction();
        string? head = GetHead(branchId, tx);
        if (head != expectedHead)
        {
            tx.Rollback();
            return (false, head, null);
        }
        string ev = AppendEvent(tx, branchId, type, payload, ruleVersion, deps, blobs, expectedHead);
        tx.Commit();
        return (true, null, ev);
    }
}

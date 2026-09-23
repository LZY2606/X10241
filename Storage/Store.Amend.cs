using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed partial class Store
{
    /// <summary>
    /// 修订一个直接来源：只新增版本，从不覆盖旧版本。然后依据依赖关系计算失效范围——
    /// 只让依赖该记录的下游结论失效（局部），不全局替换旧结论。
    /// </summary>
    public AmendResult AmendRecord(string branchId, string expectedHead, string stableId, object newPayload, string kind)
    {
        using var tx = Conn.BeginTransaction();
        string? head = GetHead(branchId, tx);
        if (head != expectedHead)
            return new AmendResult(false, head, null!, 0, new List<string>());

        int oldVersion = LatestVersionNo(tx, stableId);
        string json = JsonSerializer.Serialize(newPayload, newPayload.GetType(), JsonOpts);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        string blob = InsertBlob(bytes, "application/json", tx);
        int newVersion = oldVersion + 1;

        using (var vcmd = Conn.CreateCommand())
        {
            vcmd.Transaction = tx;
            vcmd.CommandText = @"INSERT INTO raw_versions(version_id,stable_id,version_no,payload_json,blob_sha,created_at)
                                 VALUES(@vid,@id,@no,@json,@blob,@t);";
            Add(vcmd, "@vid", $"{stableId}#v{newVersion}"); Add(vcmd, "@id", stableId);
            Add(vcmd, "@no", newVersion); Add(vcmd, "@json", json); Add(vcmd, "@blob", blob);
            Add(vcmd, "@t", IsoNow());
            vcmd.ExecuteNonQuery();
        }

        string amendEvent = AppendEvent(tx, branchId, EventTypes.RecordAmended,
            new { record = stableId, kind, oldVersion, newVersion, changeScope = "single-source" },
            currentRuleVersion(tx), depsArray: new List<string> { stableId }, sourceBlobs: new List<string> { blob });

        // 计算失效范围：找出该分支上 deps 引用该记录的分析/裁定事件。
        var downstream = FindDownstream(tx, branchId, stableId);
        foreach (var d in downstream)
        {
            MarkSuperseded(tx, d.eventId, amendEvent, scope: $"dependency:{stableId}");
            string invId = "inv." + Guid.NewGuid().ToString("N").Substring(0, 10);
            using var icmd = Conn.CreateCommand();
            icmd.Transaction = tx;
            icmd.CommandText = @"INSERT INTO invalidations(invalidation_id,downstream_event,changed_record,old_version,new_version,scope,created_at)
                                 VALUES(@id,@ev,@rec,@ov,@nv,@scope,@t);";
            Add(icmd, "@id", invId); Add(icmd, "@ev", d.eventId); Add(icmd, "@rec", stableId);
            Add(icmd, "@ov", oldVersion); Add(icmd, "@nv", newVersion);
            Add(icmd, "@scope", $"dependency:{stableId}"); Add(icmd, "@t", IsoNow());
            icmd.ExecuteNonQuery();
            AppendEvent(tx, branchId, EventTypes.Invalidated,
                new { downstreamEvent = d.eventId, changedRecord = stableId, oldVersion, newVersion, scope = d.scope },
                currentRuleVersion(tx), depsArray: new List<string> { stableId, d.eventId }, sourceBlobs: new());
        }

        tx.Commit();
        return new AmendResult(true, null, amendEvent, newVersion, downstream.Select(d => d.eventId).ToList());
    }

    private List<(string eventId, string scope)> FindDownstream(SqliteTransaction tx, string branchId, string stableId)
    {
        var hits = new List<(string, string)>();
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT event_id, type, deps_json FROM events WHERE branch_id=@b ORDER BY seq;";
        Add(cmd, "@b", branchId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string eventId = r.GetString(0);
            string type = r.GetString(1);
            string depsJson = r.GetString(2);
            var deps = JsonSerializer.Deserialize<List<string>>(dpsSafe(depsJson)) ?? new();
            if (type.StartsWith("ANALYSIS", StringComparison.Ordinal) ||
                type.StartsWith("DECISION", StringComparison.Ordinal) ||
                type.StartsWith("CANDIDATE", StringComparison.Ordinal))
            {
                if (deps.Contains(stableId)) hits.Add((eventId, $"dependency:{stableId}"));
            }
        }
        return hits;
    }

    private static string dpsSafe(string j) => string.IsNullOrWhiteSpace(j) ? "[]" : j;

    private void MarkSuperseded(SqliteTransaction tx, string downstreamEvent, string byEvent, string scope)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE events SET superseded_by=@by, superseded_scope=@scope WHERE event_id=@id;";
        Add(cmd, "@by", byEvent); Add(cmd, "@scope", scope); Add(cmd, "@id", downstreamEvent);
        cmd.ExecuteNonQuery();
    }
}

public sealed record AmendResult(bool Ok, string? ConflictWith, string EventId, int NewVersion, List<string> InvalidatedEvents);

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IsoMix.Cases;
using Microsoft.Data.Sqlite;

namespace IsoMix.Storage;

public sealed partial class Store
{
    public bool IsEmpty()
    {
        using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM raw_records;";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 0;
    }

    /// <summary>原子导入整个案例包：任一记录失败则整体回滚，绝不留下半份记录。</summary>
    public ImportOutcome ImportCasePack(CasePack pack, string? originalBytesJson = null)
    {
        if (string.IsNullOrWhiteSpace(pack.PackId))
            throw new InvalidDataException("案例包缺少 packId。");
        string rawJson = originalBytesJson ?? JsonSerializer.Serialize(pack, JsonOpts);

        using var tx = Conn.BeginTransaction();
        try
        {
            string packBlob = InsertBlob(System.Text.Encoding.UTF8.GetBytes(rawJson), "application/json", tx);

            // 先建基线分支（尚无 head）。
            string branchCreated = null!;
            if (!BranchExists(BaselineBranch, tx))
            {
                branchCreated = CreateBranchRow(tx, BaselineBranch, "基线分支", null, null, "import");
            }

            var recordIds = new List<string>();
            AddRecord(tx, "axis", "axes", pack.Axes.Select(a => ((object?)a, (string?)a.Key)));
            AddRecord(tx, "sample", "samples", pack.Samples.Select(x => ((object?)x, (string?)x.Id)));
            AddRecord(tx, "endmember-set", "endmemberSets", pack.EndmemberSets.Select(x => ((object?)x, (string?)x.Id)));
            AddRecord(tx, "covariance", "covariances", pack.Covariances.Select(x => ((object?)x, (string?)x.Id)));
            AddRecord(tx, "fractionation", "fractionations", pack.Fractionations.Select(x => ((object?)x, (string?)x.Id)));
            AddRecord(tx, "prior", "priors", pack.Priors.Select(x => ((object?)x, (string?)x.Id)));
            AddRecord(tx, "hypothesis", "hypotheses", pack.Hypotheses.Select(x => ((object?)x, (string?)x.Id)));

            // 记录场景与回放步骤作为可读取的原始材料（不改数据）。
            AddRecord(tx, "scenario", "scenarios", pack.Scenarios.Select(x => ((object?)x, (string?)x.Id)));
            AddRecord(tx, "replay", "replay", new[] { ((object?)pack.Replay, (string?)"replay") });

            var blobRefs = new List<string> { packBlob };
            var deps = recordIds;
            string importEvent = AppendEvent(tx, BaselineBranch, EventTypes.ImportBaseline,
                new
                {
                    packId = pack.PackId,
                    title = pack.Title,
                    ruleVersion = pack.RuleVersion,
                    recordCount = CountRecords(tx),
                    retainedRaw = true
                },
                pack.RuleVersion,
                depsArray: PackDeps(pack),
                sourceBlobs: blobRefs);

            SetBranchHead(tx, BaselineBranch, importEvent);
            tx.Commit();
            return new ImportOutcome(true, BaselineBranch, importEvent);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private long CountRecords(SqliteTransaction tx)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM raw_records;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static List<string> PackDeps(CasePack pack)
    {
        var deps = new List<string>();
        deps.AddRange(pack.Samples.Select(s => s.Id));
        deps.AddRange(pack.EndmemberSets.Select(e => e.Id));
        deps.AddRange(pack.Covariances.Select(c => c.Id));
        deps.AddRange(pack.Fractionations.Select(f => f.Id));
        deps.AddRange(pack.Priors.Select(p => p.Id));
        deps.AddRange(pack.Hypotheses.Select(h => h.Id));
        return deps;
    }

    private void AddRecord(SqliteTransaction tx, string kind, string collection,
        IEnumerable<(object? item, string? id)> items)
    {
        foreach (var (item, id) in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException($"{kind} 记录缺少稳定 id，导入中止并回滚。");
            string json = JsonSerializer.Serialize(item, item!.GetType(), JsonOpts);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            string blob = InsertBlob(bytes, "application/json", tx);
            using var cmd = Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO raw_records(stable_id,kind,blob_sha,byte_offset,byte_length,created_at)
                                VALUES(@id,@kind,@blob,0,@len,@t)
                                ON CONFLICT(stable_id) DO NOTHING;";
            Add(cmd, "@id", id); Add(cmd, "@kind", kind); Add(cmd, "@blob", blob);
            Add(cmd, "@len", bytes.Length); Add(cmd, "@t", IsoNow());
            cmd.ExecuteNonQuery();

            int nextVersion = LatestVersionNo(tx, id) + 1;
            using var vcmd = Conn.CreateCommand();
            vcmd.Transaction = tx;
            vcmd.CommandText = @"INSERT INTO raw_versions(version_id,stable_id,version_no,payload_json,blob_sha,created_at)
                                 VALUES(@vid,@id,@no,@json,@blob,@t);";
            Add(vcmd, "@vid", $"{id}#v{nextVersion}"); Add(vcmd, "@id", id);
            Add(vcmd, "@no", nextVersion); Add(vcmd, "@json", json);
            Add(vcmd, "@blob", blob); Add(vcmd, "@t", IsoNow());
            vcmd.ExecuteNonQuery();
        }
    }

    private bool BranchExists(string branchId, SqliteTransaction tx)
    {
        using var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE branch_id=@b;";
        Add(cmd, "@b", branchId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
}

public static class EventTypes
{
    public const string ImportBaseline = "IMPORT.BASELINE";
    public const string BranchCreated = "BRANCH.CREATED";
    public const string RecordAmended = "RECORD.AMENDED";
    public const string AnalysisComputed = "ANALYSIS.COMPUTED";
    public const string CandidateProposed = "CANDIDATE.PROPOSED";
    public const string IdentifiabilityFlagged = "IDENTIFIABILITY.FLAGGED";
    public const string DiagnosticRaised = "DIAGNOSTIC.RAISED";
    public const string DecisionAccepted = "DECISION.ACCEPTED";
    public const string DecisionRejected = "DECISION.REJECTED";
    public const string DecisionAdjusted = "DECISION.PARAM_ADJUSTED";
    public const string DecisionUndone = "DECISION.UNDONE";
    public const string ConflictDetected = "CONFLICT.DETECTED";
    public const string Invalidated = "DEPENDENCY.INVALIDATED";
}

public sealed record ImportOutcome(bool Success, string BranchId, string EventId);

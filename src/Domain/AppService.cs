using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IsotopeMix.Domain;

public sealed class ConflictException : Exception
{
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }
    public ConflictException(long expected, long actual)
        : base($"版本冲突：页面基于版本 {expected}，分支已推进到 {actual}。请先查看双方差异再决定。")
    {
        ExpectedVersion = expected; ActualVersion = actual;
    }
}

public sealed class ImportRejectedException : Exception
{
    public List<Diagnostic> Diagnostics { get; }
    public ImportRejectedException(List<Diagnostic> diagnostics)
        : base("导入被拒绝：本次变更已整体回滚。") => Diagnostics = diagnostics;
}

public sealed class AppService
{
    private readonly EventStore _store;
    private readonly Func<DateTime> _clock;

    public AppService(EventStore store, Func<DateTime>? clock = null)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public FoldedState State(long? asOf = null) => StateFolder.Fold(_store.AllEvents(), asOf);

    public byte[] RawBytes(string blobId) => _store.GetBlob(blobId) ?? Array.Empty<byte>();

    private string Now() => _clock().ToString("yyyy-MM-ddTHH:mm:ssZ");

    // ---------------- 导入（事务，失败全量回滚） ----------------

    public RawBatchImported ImportBatch(RawBatch batch, byte[] rawPayload, string origin)
    {
        var errors = batch.Diagnostics.Where(d => d.Severity == "error").ToList();
        // 致命的结构错误（空样品、空端元集）拒绝整批；量值不可转换只保留诊断。
        if (batch.Samples.Count == 0)
            errors.Add(new Diagnostic { Code = "no-samples", Message = "批次中没有任何样品记录。" });
        if (batch.EndmemberSets.Count == 0)
            errors.Add(new Diagnostic { Code = "no-endmembers", Message = "批次中没有任何端元分布。" });
        foreach (var em in batch.EndmemberSets)
            if (em.Members.Count < 2)
                errors.Add(new Diagnostic { Code = "too-few-endmembers", Where = em.Id,
                    Message = "端元分布少于两个成员，无法构成混合单纯形。" });
        if (errors.Count > 0)
            throw new ImportRejectedException(errors);

        var sha = Convert.ToHexString(SHA256.HashData(rawPayload));
        var blobId = StableIds.Deterministic("blob", batch.BatchId, sha);
        var payload = new RawBatchImported(
            batch.BatchId, batch.Label, origin, Now(), sha, blobId, batch, batch.Diagnostics);
        var ev = new EventWrite(
            StableIds.Deterministic("event", "import", batch.BatchId).ToString(),
            "raw-batch-imported", "",
            JsonSerializer.Serialize(payload, StateFolder.JsonOpts), Now());
        _store.AppendInTransaction(
            new[] { new BlobWrite(blobId, "application/octet-stream", rawPayload, sha, Now(), origin) },
            new[] { ev });
        return payload;
    }

    // ---------------- 分支 ----------------

    public sealed record BranchSpecRequest(
        string ParentBranchId, string Label, string Purpose,
        string BatchId, string SampleId, string EndmemberSetId,
        string CovarianceId, string FractionationId, string PriorId, string? Revises);

    public (BranchCreated created, AnalysisComputed analysis) CreateBranchWithAnalysis(BranchSpecRequest req)
    {
        var (created, analysis) = PrepareBranch(req);
        _store.AppendInTransaction(Array.Empty<BlobWrite>(),
            new[] { ToEvent(created, created.BranchId), ToEvent(analysis, created.BranchId) });
        return (created, analysis);
    }

    private (BranchCreated created, AnalysisComputed analysis) PrepareBranch(BranchSpecRequest req)
    {
        var state = State();
        var batch = FindBatch(state, req.BatchId);
        var sample = batch.Samples.First(s => s.Id == req.SampleId);
        var ems = batch.EndmemberSets.First(e => e.Id == req.EndmemberSetId);
        var cov = batch.Covariances.First(c => c.Id == req.CovarianceId);
        var frac = batch.Fractionations.First(f => f.Id == req.FractionationId);
        var prior = string.IsNullOrEmpty(req.PriorId) ? null : batch.Priors.First(p => p.Id == req.PriorId);

        var branchId = StableIds.Deterministic(
            "branch", req.BatchId, req.SampleId, req.EndmemberSetId, req.CovarianceId,
            req.FractionationId, req.PriorId ?? "none", req.Label).ToString();
        var parentVersion = string.IsNullOrEmpty(req.ParentBranchId) ? "0" : state.Branch(req.ParentBranchId)!.LastSeq.ToString();
        var created = new BranchCreated(
            branchId, req.ParentBranchId, parentVersion, req.Label, req.Purpose,
            req.BatchId, req.SampleId, req.EndmemberSetId, req.CovarianceId,
            req.FractionationId, req.PriorId ?? "", "all-modes", Now(), req.Revises);

        var analysis = RunAnalysis(batch, created, sample, ems, cov, frac, prior);
        return (created, analysis);
    }

    private AnalysisComputed RunAnalysis(RawBatch batch, BranchCreated branch,
        RawSample sample, RawEndmemberSet ems, RawCovariance cov, RawFractionation frac, RawPrior? prior)
    {
        var key = AnalysisFingerprint(branch.BatchId, sample.Id, ems.Id, cov.Id, frac.Id, prior?.Id ?? "");
        var result = MixingEngine.Analyze(
            new MixingEngine.Inputs { Sample = sample, EndmemberSet = ems, Covariance = cov, Fractionation = frac, Prior = prior },
            key);
        var analysisId = StableIds.Deterministic("analysis",
            MixingEngine.RuleVersion, branch.BatchId, key).ToString();
        return new AnalysisComputed(
            analysisId, branch.BranchId, MixingEngine.RuleVersion,
            batch.BatchId, sample.Id, ems.Id, cov.Id, frac.Id, prior?.Id ?? "",
            key, result, Now());
    }

    public static string AnalysisFingerprint(string batch, string sample, string ems, string cov, string frac, string prior)
        => StableIds.Deterministic("fp", batch, sample, ems, cov, frac, prior).ToString();

    private static RawBatch FindBatch(FoldedState s, string id)
        => s.Batches.First(b => b.BatchId == id).Batch;

    // ---------------- 裁定（乐观并发 + 可撤销） ----------------

    public DecisionMade Decide(string branchId, string candidateId, string outcome, string comment, long baseVersion)
    {
        var state = State();
        var branch = state.Branch(branchId) ?? throw new InvalidOperationException("分支不存在");
        if (branch.Version != baseVersion) throw new ConflictException(baseVersion, branch.Version);

        var analysis = branch.Analyses.LastOrDefault()
            ?? throw new InvalidOperationException("分支尚无分析");
        if (!analysis.Result.Candidates.Any(c => c.CandidateId == candidateId))
            throw new InvalidOperationException("候选不属于该分支分析");

        var decision = new DecisionMade(
            StableIds.Deterministic("decision", branchId, analysis.AnalysisId, candidateId, outcome, comment, branch.Version + 1).ToString(),
            branchId, analysis.AnalysisId, candidateId, outcome, comment, Now(), branch.Version + 1);
        _store.AppendInTransaction(Array.Empty<BlobWrite>(), new[] { ToEvent(decision, branchId) });
        return decision;
    }

    public DecisionReverted RevertDecision(string branchId, string decisionId)
    {
        var state = State();
        var branch = state.Branch(branchId) ?? throw new InvalidOperationException("分支不存在");
        var target = branch.Decisions.LastOrDefault()
            ?? throw new InvalidOperationException("没有可撤销的裁定");
        if (target.DecisionId != decisionId)
            throw new InvalidOperationException("只能撤销该分支最近一次裁定，以恢复前一分支状态");

        var restored = branch.Decisions.Count > 1
            ? branch.Decisions[^2].DecisionId
            : null;
        var rev = new DecisionReverted(branchId, decisionId, restored ?? "", Now());
        _store.AppendInTransaction(Array.Empty<BlobWrite>(), new[] { ToEvent(rev, branchId) });
        return rev;
    }

    // ---------------- 参数/来源修订（新建分支 + 局部失效公告） ----------------

    public (BranchCreated branch, AnalysisComputed analysis, InvalidationBulletin bulletin) ReviseParameters(
        string branchId, string kind, string newRefId, string basis)
    {
        var state = State();
        var old = state.Branch(branchId)!;
        var batch = FindBatch(state, old.Spec.BatchId);
        var req = new BranchSpecRequest(
            branchId,
            $"{old.Spec.Label} → {kind}修订",
            $"parameter-revision:{kind}",
            old.Spec.BatchId, old.Spec.SampleId, old.Spec.EndmemberSetId, old.Spec.CovarianceId,
            kind == "fractionation" ? newRefId : old.Spec.FractionationId,
            kind == "prior" ? newRefId : (old.Spec.PriorId == "" ? "" : old.Spec.PriorId),
            $"revision:{kind}:{old.Spec.FractionationId}");
        var (created, analysis) = PrepareBranch(req);
        var oldRef = kind == "fractionation" ? old.Spec.FractionationId : old.Spec.PriorId;
        var affected = ComputeAffected(state, branchId, kind, oldRef);
        var revisionId = StableIds.Deterministic("rev", "param", branchId, kind, newRefId).ToString();
        var revision = new ParameterRevision(
            revisionId, branchId, created.BranchId, kind, oldRef, newRefId, Now(), affected, basis);
        var revisionEventId = RevisionEventId("parameter-revision", revisionId);
        var bulletin = MakeBulletin(state, revisionEventId, $"parameter-revision:{kind}", affected,
            $"参数修订只使引用 {kind}={oldRef} 的分析失效；其他分支结论保持原样。");
        PersistBranchRevision(new[]
        {
            ToEvent(created, created.BranchId), ToEvent(analysis, created.BranchId),
            ToEvent(revision, branchId), ToEvent(bulletin, "")
        });
        return (created, analysis, bulletin);
    }

    public (BranchCreated branch, AnalysisComputed analysis, InvalidationBulletin bulletin) ReviseSource(
        string branchId, string refType, string newRefId, string basis)
    {
        var state = State();
        var old = state.Branch(branchId)!;
        var newEms = refType == "endmember-set" ? newRefId : old.Spec.EndmemberSetId;
        var newCov = refType == "covariance" ? newRefId : old.Spec.CovarianceId;
        var newSample = refType == "sample" ? newRefId : old.Spec.SampleId;
        var req = new BranchSpecRequest(
            branchId, $"{old.Spec.Label} → 来源修订", $"source-revision:{refType}",
            old.Spec.BatchId, newSample, newEms, newCov, old.Spec.FractionationId,
            old.Spec.PriorId == "" ? "" : old.Spec.PriorId,
            $"revision:source:{refType}");
        var (created, analysis) = PrepareBranch(req);
        var oldRef = refType switch
        {
            "endmember-set" => old.Spec.EndmemberSetId,
            "covariance" => old.Spec.CovarianceId,
            _ => old.Spec.SampleId
        };
        var affected = ComputeAffected(state, branchId, $"source:{refType}", oldRef);
        var revisionId = StableIds.Deterministic("rev", "source", branchId, refType, newRefId).ToString();
        var revision = new SourceRevision(
            revisionId, branchId, created.BranchId, refType, oldRef, newRefId, old.Spec.BatchId, Now(), affected, basis);
        var revisionEventId = RevisionEventId("source-revision", revisionId);
        var bulletin = MakeBulletin(state, revisionEventId, $"source-revision:{refType}", affected,
            $"来源修订只使引用该 {refType} 的局部依赖失效；无全局替换。");
        PersistBranchRevision(new[]
        {
            ToEvent(created, created.BranchId), ToEvent(analysis, created.BranchId),
            ToEvent(revision, branchId), ToEvent(bulletin, "")
        });
        return (created, analysis, bulletin);
    }

    private static List<string> ComputeAffected(FoldedState state, string originBranchId, string kind, string oldRef)
    {
        var affected = new List<string>();
        foreach (var b in state.Branches.Values)
        {
            bool Touches(AnalysisComputed a) => kind switch
            {
                "fractionation" => a.FractionationId == oldRef,
                "prior" => a.PriorId == oldRef,
                "source:endmember-set" => a.EndmemberSetId == oldRef,
                "source:covariance" => a.CovarianceId == oldRef,
                "source:sample" => a.SampleId == oldRef,
                _ => false
            };
            affected.AddRange(b.Analyses.Where(Touches).Select(a => a.AnalysisId));
        }
        return affected.Distinct().ToList();
    }

    private InvalidationBulletin MakeBulletin(FoldedState state, string revisionEventId, string cause,
        List<string> affected, string scope)
    {
        var branches = state.Branches.Values
            .Where(b => b.Analyses.Any(a => affected.Contains(a.AnalysisId)))
            .Select(b => b.Spec.BranchId).ToList();
        return new InvalidationBulletin(
            StableIds.Deterministic("bulletin", revisionEventId, string.Join(",", affected)).ToString(),
            Now(), cause, revisionEventId, affected, branches, scope);
    }

    private void PersistBranchRevision(IEnumerable<EventWrite> events)
        => _store.AppendInTransaction(Array.Empty<BlobWrite>(), events);

    public CounterexampleRecorded RecordCounterexample(string baselineBatchId, string scenario,
        string branchId, string expectedVerdict)
    {
        var state = State();
        var branch = state.Branch(branchId)!;
        var analysis = branch.Analyses.Last();
        var observed = analysis.Result.Verdict;
        var bounded = observed == expectedVerdict
            || (expectedVerdict == "under_identified" && observed == "under_identified");
        var rec = new CounterexampleRecorded(
            StableIds.Deterministic("archive", baselineBatchId, scenario, branchId).ToString(),
            baselineBatchId, scenario, branchId, analysis.AnalysisId,
            expectedVerdict, observed, bounded, Now(), analysis.Result.Events);
        _store.AppendInTransaction(Array.Empty<BlobWrite>(), new[] { ToEvent(rec, branchId) });
        return rec;
    }

    public static string RevisionEventId(string type, string revisionId)
        => StableIds.Deterministic("ev", type, revisionId).ToString();

    private static EventWrite ToEvent(object payload, string branchId)
    {
        var type = payload.GetType().Name;
        var json = JsonSerializer.Serialize(payload, StateFolder.JsonOpts);
        var idProp = payload.GetType().GetProperty(
            payload is BranchCreated ? nameof(BranchCreated.BranchId)
            : payload is AnalysisComputed ? nameof(AnalysisComputed.AnalysisId)
            : payload is DecisionMade ? nameof(DecisionMade.DecisionId)
            : payload is DecisionReverted ? nameof(DecisionReverted.BranchId)
            : payload is ParameterRevision ? nameof(ParameterRevision.RevisionId)
            : payload is SourceRevision ? nameof(SourceRevision.RevisionId)
            : payload is InvalidationBulletin ? nameof(InvalidationBulletin.BulletinId)
            : nameof(CounterexampleRecorded.ArchiveId))!;
        var id = idProp.GetValue(payload)!.ToString()!;
        var eventType = payload switch
        {
            BranchCreated => "branch-created",
            AnalysisComputed => "analysis-computed",
            DecisionMade => "decision-made",
            DecisionReverted => "decision-reverted",
            ParameterRevision => "parameter-revision",
            SourceRevision => "source-revision",
            InvalidationBulletin => "invalidation-bulletin",
            CounterexampleRecorded => "counterexample-recorded",
            _ => throw new ArgumentOutOfRangeException(nameof(payload))
        };
        return new EventWrite(
            StableIds.Deterministic("ev", eventType, id).ToString(), eventType,
            eventType == "invalidation-bulletin" ? "" : branchId, json,
            payload.GetType().GetProperty("At")?.GetValue(payload)?.ToString()
                ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }
}

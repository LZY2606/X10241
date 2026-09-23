using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IsoMix.Cases;
using IsoMix.Domain;
using IsoMix.Storage;

namespace IsoMix.App;

/// <summary>
/// 反演编排：从某个分支读取材料的“当时版本”，规范化为数学输入，运行 MixingMath，
/// 并把多候选、可识别性判定、诊断与不可变结果工件写入事件日志（不动原始数据）。
/// </summary>
public sealed class AnalysisService
{
    private readonly Store _store;
    public AnalysisService(Store store) => _store = store;

    public sealed class AnalysisRun
    {
        public CasePack Pack { get; init; } = null!;
        public HypothesisSpec Hypothesis { get; init; } = null!;
        public Normalizer.Prepared Prepared { get; init; } = null!;
        public MixingResult Result { get; init; } = null!;
    }

    public CasePack LoadPackSnapshot()
    {
        // 以基线上最新版本重建内存中的 CasePack（旧版本仍可单独读取）。
        var embedded = CaseLoader.LoadEmbedded();
        T Latest<T>(string id, string kind, T fallback)
        {
            var json = _store.LatestPayload(id);
            if (json is null) return fallback;
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        var pack = new CasePack
        {
            PackId = embedded.PackId,
            Title = embedded.Title,
            Narrative = embedded.Narrative,
            RuleVersion = embedded.RuleVersion,
            Axes = embedded.Axes,
            Samples = embedded.Samples.Select(s => Latest(s.Id, "sample", s)).ToList(),
            EndmemberSets = embedded.EndmemberSets.Select(e => Latest(e.Id, "endmember-set", e)).ToList(),
            Covariances = embedded.Covariances.Select(c => Latest(c.Id, "covariance", c)).ToList(),
            Fractionations = embedded.Fractionations.Select(f => Latest(f.Id, "fractionation", f)).ToList(),
            Priors = embedded.Priors.Select(p => Latest(p.Id, "prior", p)).ToList(),
            Hypotheses = embedded.Hypotheses,
            Scenarios = embedded.Scenarios,
            Replay = embedded.Replay,
            Confidence = embedded.Confidence
        };
        return pack;
    }

    public AnalysisRun Prepare(string hypothesisId, bool multiRegionDemo = false)
    {
        var pack = LoadPackSnapshot();
        var hyp = pack.Hypotheses.First(h => h.Id == hypothesisId);
        var sample = pack.Samples.First(s => s.Id == hyp.Sample);
        var emset = pack.EndmemberSets.First(e => e.Id == hyp.EndmemberSet);
        var frac = pack.Fractionations.First(f => f.Id == hyp.Fractionation);
        var cov = pack.Covariances.FirstOrDefault(c => c.Id == (sample.Covariance));
        PriorSpec? prior = hyp.Prior is null ? null : pack.Priors.First(p => p.Id == hyp.Prior);

        var prepared = Normalizer.Prepare(sample, emset, cov, frac, prior, pack.Axes, hyp.MissingAxes, multiRegionDemo);
        var result = MixingMath.Solve(prepared.Input);
        // 规范化阶段的诊断并入结果。
        foreach (var d in prepared.Diagnostics)
            if (!result.Diagnostics.Any(x => x.Code == d.Code)) result.Diagnostics.Insert(0, d);

        return new AnalysisRun { Pack = pack, Hypothesis = hyp, Prepared = prepared, Result = result };
    }

    /// <summary>在分支上记录一次反演：分析事件 + 候选事件 + 可识别性/诊断事件，保存结果工件。单事务。</summary>
    public (string analysisEvent, string artifactId) RecordRun(string branchId, string expectedHead, string hypothesisId, bool multiRegionDemo = false)
    {
        var run = Prepare(hypothesisId, multiRegionDemo);
        var result = run.Result;
        var deps = BuildDeps(run);
        var blobs = SourceBlobs(deps);

        using var tx = _store.Conn.BeginTransaction();
        string? head = _store.GetHead(branchId, tx);
        if (head != expectedHead)
        {
            tx.Rollback();
            throw new ConcurrencyException(branchId, head ?? "(空)");
        }

        string analysisEvent = _store.AppendEvent(tx, branchId, EventTypes.AnalysisComputed,
            new
            {
                hypothesis = hypothesisId,
                usedAxes = result.Axes,
                identifiable = result.Identifiable,
                feasible = result.Feasible,
                sampleInsideEnvelope = result.SampleInsideEnvelope,
                priorInfeasible = result.PriorInfeasible,
                componentCount = result.Components.Count,
                conditionNumber = ConditionText(result.ConditionNumber),
                bestMahalanobisSquared = result.BestMahalanobisSquared,
                rmsResidual = result.RmsResidual
            },
            run.Pack.RuleVersion, deps, blobs, atEvent: expectedHead);

        foreach (var comp in result.Components)
            _store.AppendEvent(tx, branchId, EventTypes.CandidateProposed,
                new
                {
                    hypothesis = hypothesisId,
                    componentIndex = comp.Index,
                    fractions = comp.Representative?.Fractions,
                    endmemberIds = result.EndmemberIds,
                    mahalanobisSquared = comp.Representative?.MahalanobisSquared,
                    status = "proposed"
                },
                run.Pack.RuleVersion, deps, blobs);

        if (!result.Identifiable)
        {
            var flag = result.Diagnostics.First(d => d.Code is MixingMath.IdNearCollinear or MixingMath.IdUnderdetermined);
            _store.AppendEvent(tx, branchId, EventTypes.IdentifiabilityFlagged,
                new
                {
                    hypothesis = hypothesisId,
                    code = flag.Code,
                    severity = flag.Severity.ToString(),
                    message = flag.Message,
                    affected = flag.AffectedObjectIds,
                    machineReadable = new
                    {
                        rule = "no-clip-then-renormalize",
                        constraintsEnforced = new[] { "sum_to_one", "nonnegative" },
                        correlatedErrors = "covariance_propagation",
                        singularValues = result.SingularValues,
                        conditionNumber = ConditionText(result.ConditionNumber)
                    }
                },
                run.Pack.RuleVersion, deps, blobs);
        }

        foreach (var d in result.Diagnostics)
            _store.AppendEvent(tx, branchId, EventTypes.DiagnosticRaised,
                new
                {
                    hypothesis = hypothesisId,
                    code = d.Code,
                    severity = d.Severity.ToString(),
                    message = d.Message,
                    affected = d.AffectedObjectIds
                },
                run.Pack.RuleVersion, deps, blobs);

        string artifactId = _store.SaveArtifactTx(tx, branchId, analysisEvent, "mixing-result", ResultDto.Of(result, hypothesisId));
        tx.Commit();
        return (analysisEvent, artifactId);
    }

    public static List<string> BuildDeps(AnalysisRun run)
    {
        var deps = new List<string> { run.Hypothesis.Sample, run.Hypothesis.EndmemberSet, run.Hypothesis.Fractionation };
        if (run.Hypothesis.Prior is not null) deps.Add(run.Hypothesis.Prior);
        var sample = run.Pack.Samples.First(s => s.Id == run.Hypothesis.Sample);
        if (sample.Covariance is not null) deps.Add(sample.Covariance);
        return deps.Distinct().ToList();
    }

    private List<string> SourceBlobs(List<string> deps)
    {
        var blobs = new List<string>();
        foreach (var id in deps)
        {
            var versions = _store.Versions(id);
            if (versions.Count > 0) blobs.Add(versions[^1].BlobSha);
        }
        return blobs;
    }

    private static string ConditionText(double c) => double.IsInfinity(c) || double.IsNaN(c) ? "Infinity" : c.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ConcurrencyException : Exception
{
    public string BranchId { get; }
    public string CurrentHead { get; }
    public ConcurrencyException(string branch, string head) : base($"分支 {branch} 已被他人更新（当前头 {head}）。")
    { BranchId = branch; CurrentHead = head; }
}

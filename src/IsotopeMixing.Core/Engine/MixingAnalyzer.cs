using System;
using System.Collections.Generic;
using System.Linq;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Engine;

public sealed partial class MixingAnalyzer
{
    public const string RuleVersion = "rule.feasibility.v1";

    /// <summary>近共线阈值：白化后最小奇异值小于此值即判定可识别性不足（无量纲，单位无关）。</summary>
    public const double NearCollinearSmin = 0.8;
    public const double RankDeficientSmin = 1e-8;

    private static double CorrelationThreshold = 0.05;

    public AnalysisResult Analyze(
        Sample sample,
        IReadOnlyList<Endmember> endmembers,
        AnalysisRequest request,
        IReadOnlyDictionary<string, FractionationTable> fractionationTables)
    {
        var prepared = InputPreparer.Prepare(sample, endmembers);
        int n = prepared.ObservedTracers.Length;
        int m = endmembers.Count;
        int d = m - 1;
        var shared = new List<Judgment>(prepared.Diagnostics);

        double[,]? cov = BuildCovariance(prepared, shared);

        var bounds = request.Priors?.Select(p =>
        {
            int idx = prepared.EndmemberOrder.ToList().IndexOf(p.EndmemberId);
            return (idx, p.Min, p.Max);
        }).Where(t => t.idx >= 0).ToList();

        var candidates = new List<CandidateSolution>();
        foreach (var hyp in request.Hypotheses)
        {
            var region = AnalyzeHypothesis(prepared, cov, hyp, bounds, request.ConfidenceLevel, fractionationTables);
            double[,] eff = EffectiveEndmemberMatrix(prepared, hyp, fractionationTables);
            string emSig = string.Join("|", prepared.EndmemberOrder.OrderBy(x => x));
            string frSig = string.Join("|", hyp.TracerToVersion.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            candidates.Add(new CandidateSolution
            {
                CandidateId = $"cand-{DeterministicIds.ShortHash(sample.SampleId + "|" + hyp.HypothesisId + "|" + emSig + "|" + frSig)}",
                HypothesisId = hyp.HypothesisId,
                EndmemberSetVersionId = emSig,
                FractionationSetSignature = frSig,
                Region = region,
                Adopted = false
            });
        }

        // 可识别性：白化 SVD（基于第一个假设的有效端元；近共线是端元几何属性）
        var firstEff = EffectiveEndmemberMatrix(prepared, request.Hypotheses[0], fractionationTables);
        var ident = AssessIdentifiability(firstEff, prepared.Observed, cov, n, m, shared, sample.SampleId);

        bool disjoint = DetectDisjoint(candidates);
        if (disjoint)
            shared.Add(new Judgment
            {
                Code = JudgmentCodes.MultipleDisjointRegions,
                Severity = "warning",
                Message = "不同分馏/端元假设产生互不相交的可行区域，全部候选保留，等待人工裁定。",
                Metrics = new Dictionary<string, double?> { ["candidateCount"] = candidates.Count },
                RefObjectId = sample.SampleId
            });

        return new AnalysisResult
        {
            SampleId = sample.SampleId,
            ObservedTracerOrder = prepared.ObservedTracers,
            Identifiability = ident,
            RuleVersion = RuleVersion,
            ConfidenceLevel = request.ConfidenceLevel,
            Candidates = candidates,
            SharedJudgments = shared,
            MultipleDisjointRegions = disjoint
        };
    }

    private static double[,] BuildCovariance(PreparedInputs prepared, List<Judgment> shared)
    {
        int n = prepared.ObservedTracers.Length;
        var cov = new double[n, n];
        if (prepared.SampleCovariance is not null)
        {
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    cov[i, j] += prepared.SampleCovariance[i, j];
        }
        // 端元协方差按比例传播：E[f] 一阶近似处取 1/m（诊断中记录所用近似点）
        double w = 1.0 / Math.Max(1, prepared.EndmemberOrder.Count);
        foreach (var emc in prepared.EndmemberCovariances)
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    cov[i, j] += emc[i, j] * w * w;
        if (n >= 2)
        {
            double maxCorr = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (cov[i, i] > 0 && cov[j, j] > 0)
                        maxCorr = Math.Max(maxCorr, Math.Abs(cov[i, j] / Math.Sqrt(cov[i, i] * cov[j, j])));
            if (maxCorr >= CorrelationThreshold)
                shared.Add(new Judgment
                {
                    Code = JudgmentCodes.CorrelatedErrorPropagated,
                    Severity = "info",
                    Message = $"测量误差存在相关（最大 |r|={maxCorr:F3}），可行域按完整协方差传播，而非独立误差近似。",
                    Metrics = new Dictionary<string, double?> { ["maxAbsCorrelation"] = maxCorr },
                });
        }
        // 零方差对角兜底（避免奇异）
        for (int i = 0; i < n; i++)
            if (cov[i, i] <= 0) cov[i, i] = 0.25; // 0.5 per mil 的保守测量不确定度
        return cov;
    }

    private static double[,] EffectiveEndmemberMatrix(
        PreparedInputs prepared,
        FractionationHypothesis hypothesis,
        IReadOnlyDictionary<string, FractionationTable> tables)
    {
        int mc = prepared.EndmemberOrder.Count;
        int n = prepared.ObservedTracers.Length;
        var eff = new double[mc, n];
        for (int k = 0; k < mc; k++)
            for (int j = 0; j < n; j++)
            {
                string tracer = prepared.ObservedTracers[j];
                double v = prepared.EndmemberValues[k, j];
                if (hypothesis.TracerToVersion.TryGetValue(tracer, out var versionId)
                    && tables.TryGetValue(versionId, out var table)
                    && table.ShiftsByEndmember.TryGetValue(prepared.EndmemberOrder[k], out var shift))
                {
                    v += shift;
                }
                eff[k, j] = v;
            }
        return eff;
    }
}

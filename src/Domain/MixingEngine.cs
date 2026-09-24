using System.Text.Json.Nodes;

namespace IsotopeMix.Domain;

public static class MixingEngine
{
    public const string RuleVersion = "mixing-rules-2026.09";
    private const double NearCollinearCondition = 25.0;
    private const double SingularFloor = 1e-9;
    private const double Z95 = 1.959963984540054;

    public sealed class Inputs
    {
        public required RawSample Sample { get; init; }
        public required RawEndmemberSet EndmemberSet { get; init; }
        public required RawCovariance Covariance { get; init; }
        public required RawFractionation Fractionation { get; init; }
        public RawPrior? Prior { get; init; }
    }

    public static MixingResult Analyze(Inputs inputs, string dependencyKey)
    {
        var result = new MixingResult { AnalysisKey = dependencyKey, MemberCount = inputs.EndmemberSet.Members.Count };

        var dims = new List<string> { "d13C", "d15N", "d18O" };
        var missing = dims.Where(d =>
        {
            var ok = inputs.Sample.Deltas.TryGetValue(d, out var q) && q.Usable;
            return !ok;
        }).ToList();
        var usable = dims.Where(d => !missing.Contains(d)).ToList();

        var sample = usable.Select(d =>
        {
            var q = inputs.Sample.Deltas[d];
            var shift = inputs.Fractionation.Shift.TryGetValue(d, out var s) ? s : 0.0;
            return q.Value!.Value - shift;
        }).ToArray();

        // 协方差按分馏常数校正不变；相关误差原样进入马氏距离传播。
        var cov = ExtractCovariance(inputs.Covariance, usable);

        var modes = inputs.EndmemberSet.Members
            .GroupBy(m => m.ModeId ?? "default")
            .Select(g => new
            {
                ModeId = g.Key,
                Members = g.ToList()
            })
            .ToList();

        foreach (var mode in modes)
        {
            var bad = mode.Members.Where(m => usable.Any(d => !m.Means.TryGetValue(d, out var q) || !q.Usable)).ToList();
            if (bad.Count > 0)
            {
                result.Events.Add(new JudgmentEvent
                {
                    Code = "endmember-dimension-unusable",
                    Severity = "warning",
                    Scope = "mode",
                    TargetId = $"{inputs.EndmemberSet.Id}:{mode.ModeId}",
                    Statement = "该模式下端元在可用同位素维上记录缺失或不可转换，跳过此模式。",
                    Evidence = new JsonObject
                    {
                        ["members"] = JudgmentEvent.Arr(bad.Select(b => b.Code)),
                        ["usableDimensions"] = JudgmentEvent.Arr(usable)
                    }
                });
                continue;
            }

            var matrix = mode.Members.Select(m => usable.Select(d =>
            {
                var q = m.Means[d];
                var shift = inputs.Fractionation.Shift.TryGetValue(d, out var s) ? s : 0.0;
                return q.Value!.Value - shift;
            }).ToArray()).ToList();

            var request = new MixingRequest
            {
                RuleVersion = RuleVersion,
                SampleCode = inputs.Sample.Code,
                Sample = sample,
                Covariance = cov,
                CovarianceCorrelated = inputs.Covariance.HasCorrelation,
                Dimensions = usable,
                MissingDimensions = missing,
                Endmembers = matrix,
                MemberCodes = mode.Members.Select(m => m.Code).ToList(),
                ModeId = mode.ModeId,
                PriorBounds = inputs.Prior?.Bounds
            };

            var solver = new MixingSolver(request, Z95);
            var candidateId = StableIds.Deterministic(
                "candidate", dependencyKey, mode.ModeId,
                string.Join(",", mode.Members.Select(m => m.Code)),
                inputs.Prior?.Id ?? "no-prior");
            var candidate = solver.SolveCandidate(candidateId, mode.ModeId);
            AttachResidualEvidence(candidate, solver, usable);
            result.Candidates.Add(candidate);
        }

        // ---- 可识别性：中心矩阵 A (k × (n-1)) 的奇异值与条件数 ----
        var refMode = result.Candidates.FirstOrDefault();
        if (refMode is not null)
        {
            var n = result.MemberCount;
            var k = usable.Count;
            var membersForId = inputs.EndmemberSet.Members;
            var aRows = k;
            var aCols = Math.Max(1, n - 1);
            var centered = new double[aRows, aCols];
            for (var r = 0; r < k; r++)
            for (var c = 0; c < n - 1; c++)
            {
                var em = membersForId[c].Means[usable[r]]!.Value!.Value;
                var em0 = membersForId[n - 1].Means[usable[r]]!.Value!.Value;
                var shift = inputs.Fractionation.Shift.TryGetValue(usable[r], out var sh) ? sh : 0.0;
                centered[r, c] = em - em0;
            }
            var sv = LinearAlgebra.SingularValues(centered);
            var svMax = sv.FirstOrDefault();
            var svMin = sv.Where(v => v > SingularFloor).DefaultIfEmpty(0).Min();
            var rank = sv.Count(v => v > Math.Max(SingularFloor, svMax * 1e-9));
            var cond = svMin > 0 ? svMax / svMin : double.PositiveInfinity;
            result.EffectiveDimensions = k;
            result.ConditionNumber = double.IsInfinity(cond) ? 1e12 : cond;

            if (missing.Count > 0)
                result.Events.Add(new JudgmentEvent
                {
                    Code = "missing-isotope-dimension",
                    Severity = "warning",
                    Scope = "sample",
                    TargetId = inputs.Sample.Id,
                    Statement = "样品缺失同位素维，仅在已测子空间中评价可识别性；缺失维不插补。",
                    Evidence = new JsonObject
                    {
                        ["missing"] = JudgmentEvent.Arr(missing),
                        ["usable"] = JudgmentEvent.Arr(usable)
                    }
                });

            if (rank < n - 1 || k < n - 1)
            {
                result.Verdict = "under_identified";
                result.Events.Add(new JudgmentEvent
                {
                    Code = "endmembers-near-collinear",
                    Severity = "error",
                    Scope = "endmember-set",
                    TargetId = inputs.EndmemberSet.Id,
                    Statement = "端元近共线（中心矩阵秩不足），混合比例无法唯一决定；报告可识别性不足并保留全部合法候选，禁止单点答案。",
                    Evidence = new JsonObject
                    {
                        ["rank"] = rank,
                        ["requiredRank"] = n - 1,
                        ["effectiveDimensions"] = k,
                        ["singularValues"] = JudgmentEvent.Arr(sv)
                    }
                });
            }
            else if (cond > NearCollinearCondition)
            {
                result.Verdict = "under_identified";
                result.Events.Add(new JudgmentEvent
                {
                    Code = "endmembers-near-collinear",
                    Severity = "warning",
                    Scope = "endmember-set",
                    TargetId = inputs.EndmemberSet.Id,
                    Statement = "端元近共线（条件数过大），比例方差被放大，判为可识别性不足；仅给可行域不给单点。",
                    Evidence = new JsonObject
                    {
                        ["conditionNumber"] = result.ConditionNumber,
                        ["threshold"] = NearCollinearCondition,
                        ["singularValues"] = JudgmentEvent.Arr(sv)
                    }
                });
            }
        }

        EmitCandidateEvents(result, inputs, usable);

        var anyFeasible = result.Candidates.Any(c => c.Status == "feasible");
        var anyPriorInfeasible = result.Candidates.Any(c => c.Status == "prior_infeasible");
        result.Verdict ??= anyFeasible ? "identifiable" : (anyPriorInfeasible ? "infeasible" : "infeasible");
        if (result.Candidates.Count(c => c.Status == "feasible") >= 2)
            result.Events.Add(new JudgmentEvent
            {
                Code = "multiple-feasible-regions",
                Severity = "info",
                Scope = "sample",
                TargetId = inputs.Sample.Id,
                Statement = "多个端元模式均给出可行混合，存在多个可行区；全部保留等待人工裁定。",
                Evidence = new JsonObject
                {
                    ["feasibleCandidates"] = JudgmentEvent.Arr(result.Candidates.Where(c => c.Status == "feasible").Select(c => c.CandidateId))
                }
            });

        if (inputs.Covariance.HasCorrelation)
            result.Events.Add(new JudgmentEvent
            {
                Code = "correlated-error-propagated",
                Severity = "info",
                Scope = "covariance",
                TargetId = inputs.Covariance.Id,
                Statement = "测量误差存在跨维相关，已按完整协方差矩阵进入 χ²/可行域传播，而非各维独立平方和。",
                Evidence = new JsonObject
                {
                    ["dimensions"] = JudgmentEvent.Arr(usable),
                    ["offDiagonalPresent"] = true
                }
            });

        return result;
    }

    private static double[][] ExtractCovariance(RawCovariance cov, List<string> usable)
    {
        var k = usable.Count;
        var m = new double[k][];
        for (var i = 0; i < k; i++)
        {
            m[i] = new double[k];
            for (var j = 0; j < k; j++)
            {
                var ri = cov.Dimensions.IndexOf(usable[i]);
                var rj = cov.Dimensions.IndexOf(usable[j]);
                m[i][j] = ri >= 0 && rj >= 0 ? cov.Matrix[ri][rj] : (i == j ? 0.25 : 0.0);
            }
        }
        return m;
    }

    private static void AttachResidualEvidence(CandidateResult candidate, MixingSolver solver, List<string> dims)
    {
        if (candidate.Residuals is null) return;
        candidate.Warnings.Add(string.Join("; ",
            dims.Zip(candidate.Residuals, (d, r) => $"{d} 残差={r:F2}‰")));
    }

    private static void EmitCandidateEvents(MixingResult result, Inputs inputs, List<string> dims)
    {
        foreach (var c in result.Candidates)
        {
            if (c.Status == "infeasible")
                result.Events.Add(new JudgmentEvent
                {
                    Code = "sample-outside-envelope",
                    Severity = "error",
                    Scope = "candidate",
                    TargetId = c.CandidateId,
                    Statement = "样品在给定置信域下位于端元包络之外，单纯形内无可行比例；不伪造非负归一解。",
                    Evidence = new JsonObject
                    {
                        ["chi2Min"] = c.Chi2Min,
                        ["chi2Threshold"] = c.Threshold,
                        ["dimensions"] = JudgmentEvent.Arr(dims)
                    }
                });
            if (c.Status == "prior_infeasible")
                result.Events.Add(new JudgmentEvent
                {
                    Code = "infeasible-prior",
                    Severity = "error",
                    Scope = "prior",
                    TargetId = inputs.Prior?.Id ?? "",
                    Statement = "先验区间与“和为一”单纯形不相交，该先验不可行；不通过截断/重归一化制造解。",
                    Evidence = new JsonObject
                    {
                        ["prior"] = inputs.Prior?.Id ?? "",
                        ["bounds"] = new JsonObject((inputs.Prior?.Bounds ?? new())
                        .Select(kv => KeyValuePair.Create<string, JsonNode?>(kv.Key,
                            JudgmentEvent.Arr(kv.Value))))
                    }
                });
        }
    }
}

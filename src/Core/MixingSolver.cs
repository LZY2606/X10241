using System.Globalization;
using System.Text;

namespace IsotopeMixing.Core;

/// <summary>
/// 线性同位素混合模型：y = E f + Δ + ε，Σ f = 1，f ≥ 0。
/// 比例约束以等式 KKT 方式严格求解，绝不使用“先截断再归一化”的伪造方式。
/// 相关误差通过完整协方差矩阵传播；近共线/缺秩时显式报告可识别性不足。
/// </summary>
public sealed partial class MixingSolver
{
    public const double NearCollinearRatio = 0.05;
    public const double FractionEps = 1e-8;

    public MixingResult Solve(MixingRequest req)
    {
        int d = req.Dimension;
        int k = req.Sources.Count;
        var dimLabels = Enumerable.Range(0, d).Select(i => (i + 1).ToString(CultureInfo.InvariantCulture)).ToList();

        var presentDims = new List<int>();
        var missingDims = new List<int>();
        for (int i = 0; i < d; i++)
        {
            bool sourcesOk = req.Sources.All(s => s.Present[i]);
            if (req.SamplePresent[i] && sourcesOk) presentDims.Add(i);
            else missingDims.Add(i);
        }

        var judgments = new List<MachineJudgment>();
        if (presentDims.Count == 0)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.MissingDimensions, "error", false, req.SampleId,
                "样品与端元没有共同可用的同位素维，无法求解",
                Dict("dimensionCount", d, "present", 0)));
            return new MixingResult(req.SampleId, req.SampleLabel, MixingStatus.MissingDimensions,
                req.FractionationVersion, 0, 0, null, true, false, false, false,
                double.PositiveInfinity, Array.Empty<CandidateMix>(), Array.Empty<FeasibleVertex>(),
                Array.Empty<string>(), dimLabels, judgments);
        }

        if (missingDims.Count > 0)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.MissingDimensions, "warning", false, req.SampleId,
                "存在缺失同位素维，仅在共同维上求解，候选不唯一可能被放大",
                Dict("missing", string.Join(",", missingDims.Select(i => dimLabels[i])),
                     "used", presentDims.Count)));
        }

        int dd = presentDims.Count;
        double[][] B = new double[dd][];
        double[] z = new double[dd];
        for (int r = 0; r < dd; r++)
        {
            int i = presentDims[r];
            B[r] = req.Sources.Select(s => s.Mean[i]!.Value).ToArray();
            z[r] = req.SampleMean[i]!.Value - req.Fractionation[i];
        }

        // 完整协方差传播：只取共同维对应子矩阵。
        double[][] C = SubMatrix(req.SampleCovariance, presentDims);
        double[][] W = LinearAlgebra.Inverse(C);

        // 可识别性：对 [B | 1] 做奇异值分析。
        double[][] aug = new double[dd][];
        for (int r = 0; r < dd; r++)
        {
            aug[r] = new double[k + 1];
            Array.Copy(B[r], aug[r], k);
            aug[r][k] = 1.0;
        }
        double[] sv = LinearAlgebra.SingularValues(aug);
        double smax = sv.Max();
        double smin = sv[0];
        double ratio = smax > 0 ? smin / smax : 0;
        bool rankDeficient = smin <= smax * 1e-10 || dd + 1 < k;
        bool nearCollinear = ratio < NearCollinearRatio;

        if (rankDeficient)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.NearCollinear, "error", false, req.SampleId,
                "端元矩阵秩不足：比例无法唯一决定，报告全部合法候选而非单点答案",
                Dict("minSingularRatio", ratio, "sources", k, "dimensions", dd,
                     "threshold", NearCollinearRatio)));
        }
        else if (nearCollinear)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.NearCollinear, "warning", false, req.SampleId,
                "端元近共线：可识别性不足，约束解对测量误差极敏感",
                Dict("minSingularRatio", ratio, "sources", k, "dimensions", dd,
                     "threshold", NearCollinearRatio)));
        }
        else
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.NearCollinear, "info", true, req.SampleId,
                "端元矩阵满秩且条件良好",
                Dict("minSingularRatio", ratio, "threshold", NearCollinearRatio)));
        }

        var candidates = EnumerateCandidates(req, B, z, W, C, presentDims, judgments);
        bool outsideEnvelope = candidates.All(c => !c.WithinEnvelope) && candidates.Count > 0;
        bool priorInfeasible = candidates.Count > 0 && candidates.All(c => !c.PriorSatisfied);

        // 可行区域顶点（带和为一/非负的凸包顶点，标记卡方接受与否）。
        var vertices = EnumerateVertices(req, B, z, W);

        var status = DecideStatus(presentDims.Count, d, rankDeficient, nearCollinear,
            outsideEnvelope, priorInfeasible, candidates);

        AddConstraintJudgments(req, candidates, judgments);

        double envM2 = candidates.Count == 0 ? double.PositiveInfinity :
            candidates.Where(c => c.WithinEnvelope).Select(c => c.Chi2)
                      .DefaultIfEmpty(candidates.Min(c => c.Chi2)).Min();

        return new MixingResult(
            req.SampleId, req.SampleLabel, status, req.FractionationVersion,
            dd, d, ratio, rankDeficient, nearCollinear, outsideEnvelope, priorInfeasible,
            envM2, candidates, vertices,
            presentDims.Select(i => dimLabels[i]).ToList(),
            missingDims.Select(i => dimLabels[i]).ToList(), judgments);
    }

    private static IReadOnlyDictionary<string, object?> Dict(params object[] kv)
    {
        var d = new Dictionary<string, object?>();
        for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    private static double[][] SubMatrix(double[][] m, List<int> idx)
    {
        int n = idx.Count;
        double[][] r = new double[n][];
        for (int a = 0; a < n; a++)
        {
            r[a] = new double[n];
            for (int b = 0; b < n; b++) r[a][b] = m[idx[a]][idx[b]];
        }
        return r;
    }
}

public sealed partial class MixingSolver
{
    private List<CandidateMix> EnumerateCandidates(
        MixingRequest req, double[][] B, double[] z, double[][] W, double[][] C,
        List<int> presentDims, List<MachineJudgment> judgments)
    {
        int k = req.Sources.Count;
        int dd = B.Length;
        var result = new List<CandidateMix>();

        bool correlated = C.Length > 1 &&
            Enumerable.Range(0, C.Length).Any(i =>
                Enumerable.Range(0, i).Any(j => Math.Abs(C[i][j]) > 1e-15));
        if (correlated)
        {
            double rho = C[0][1] / Math.Sqrt(C[0][0] * C[1][1]);
            judgments.Add(new MachineJudgment(
                RuleCodes.CorrelatedError, "info", true, req.SampleId,
                "测量协方差含非零相关项，按完整协方差矩阵传播而非独立误差近似",
                Dict("rho01", rho)));
        }

        int maxSubset = Math.Min(k, dd + 1);
        int candNo = 0;
        for (int s = 2; s <= maxSubset; s++)
        {
            foreach (int[] combo in Combinations(k, s))
            {
                // 子集 B_s：dd x s
                double[][] Bs = B.Select(row => combo.Select(j => row[j]).ToArray()).ToArray();
                if (!ConstrainedLeastSquares(Bs, z, W, out double[] f, out double[][] fCov))
                    continue;

                bool nonnegative = f.All(v => v >= -FractionEps);
                if (!nonnegative) continue;
                for (int t = 0; t < s; t++) f[t] = Math.Max(0, f[t]);

                double[] fullF = new double[k];
                for (int t = 0; t < s; t++) fullF[combo[t]] = f[t];

                double[] fitted = LinearAlgebra.MultiplyVec(B, fullF);
                double[] resid = z.Zip(fitted, (a, b) => a - b).ToArray();
                double[] wr = LinearAlgebra.MultiplyVec(W, resid);
                double chi2 = resid.Zip(wr, (a, b) => a * b).Sum();
                int dof = Math.Max(0, dd - s + 1);
                double p = ChiSquare.PValue(chi2, dof);
                double crit = ChiSquare.Critical95(Math.Max(1, dof));
                bool within = chi2 <= (dof == 0 ? 1e-8 : crit);

                bool priorOk = true;
                var priorBreaches = new List<string>();
                for (int t = 0; t < s; t++)
                {
                    int src = combo[t];
                    double? lo = req.PriorLower[src], hi = req.PriorUpper[src];
                    if (lo is { } l && f[t] < l - 1e-6)
                    {
                        priorOk = false;
                        priorBreaches.Add($"{req.Sources[src].SourceId}:f={f[t]:G4}<{l}");
                    }
                    if (hi is { } h && f[t] > h + 1e-6)
                    {
                        priorOk = false;
                        priorBreaches.Add($"{req.Sources[src].SourceId}:f={f[t]:G4}>{h}");
                    }
                }

                var flags = new List<string>();
                if (!within) flags.Add(RuleCodes.OutsideEnvelope);
                if (!priorOk) flags.Add(RuleCodes.InfeasiblePrior);
                if (s < k) flags.Add("zero-mass-endmembers:" + string.Join("|",
                    Enumerable.Range(0, k).Except(combo).Select(j => req.Sources[j].SourceId)));

                // 比例和为一在等式层面强制；此处仅做证据记录，绝不做截断归一化。
                double sumF = f.Sum();
                if (Math.Abs(sumF - 1) > 1e-7)
                {
                    // 数值上不应发生（KKT 等式约束）；发生即拒绝并留痕。
                    flags.Add("simplex-sum-violation");
                    within = false;
                }

                candNo++;
                string id = DeterministicId("cand",
                    req.SampleId, req.FractionationVersion,
                    string.Join(".", combo.Select(j => req.Sources[j].SourceId)));

                result.Add(new CandidateMix(
                    id, req.SampleId,
                    combo.Select(j => req.Sources[j].SourceId).ToList(),
                    f.ToList(), ExpandCov(fCov, combo, k),
                    resid, chi2, p, dof, within, priorOk,
                    Accepted: within && priorOk, flags));

                if (!priorOk)
                {
                    judgments.Add(new MachineJudgment(
                        RuleCodes.InfeasiblePrior, "error", false,
                        $"{req.SampleId}/{id}",
                        "先验边界与该候选互斥，候选保留但标记为不可行先验",
                        Dict("breaches", string.Join(";", priorBreaches), "chi2", chi2)));
                }
            }
        }

        if (result.All(c => !c.WithinEnvelope) && result.Count > 0)
        {
            double best = result.Min(c => c.Chi2);
            judgments.Add(new MachineJudgment(
                RuleCodes.OutsideEnvelope, "error", false, req.SampleId,
                "样品在置信包络之外：任何单纯形内混合都无法在给定相关误差下解释样品",
                Dict("minChi2", best,
                     "critical95", ChiSquare.Critical95(Math.Max(1, dd)),
                     "dimensions", dd)));
        }

        var accepted = result.Where(c => c.Accepted).ToList();
        if (accepted.Count > 1)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.MultipleFeasibleRegions, "warning", false, req.SampleId,
                "存在多个通过卡方与先验检验的合法候选区间，全部保留供裁定",
                Dict("acceptedCandidateCount", accepted.Count,
                     "candidateIds", string.Join(",", accepted.Select(c => c.CandidateId)))));
        }
        else if (accepted.Count == 1 && result.Count(c => c.WithinEnvelope) > 1)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.MultipleFeasibleRegions, "info", true, req.SampleId,
                "存在多个包络内候选，但仅一个满足先验",
                Dict("candidateId", accepted[0].CandidateId)));
        }
        else if (accepted.Count == 1)
        {
            judgments.Add(new MachineJudgment(
                RuleCodes.MultipleFeasibleRegions, "info", true, req.SampleId,
                "恰有一个合法候选",
                Dict("candidateId", accepted[0].CandidateId)));
        }

        return result;
    }

    /// <summary>
    /// 等式约束最小二乘：min 1/2 (Bf-z)^T W (Bf-z)，约束 1^T f = 1。
    /// KKT：[B^TWB  1][f]   [B^T W z]
    ///      [1^T    0][λ] = [1       ]
    /// 比例非负性由调用方检验——不满足的组合直接丢弃，而不是截断后归一化。
    /// </summary>
    internal static bool ConstrainedLeastSquares(
        double[][] Bs, double[] z, double[][] W,
        out double[] f, out double[][] fCov)
    {
        int s = Bs[0].Length, dd = Bs.Length;
        double[][] BtW = new double[s][];
        for (int j = 0; j < s; j++)
        {
            BtW[j] = new double[dd];
            for (int i = 0; i < dd; i++)
                for (int p = 0; p < dd; p++)
                    BtW[j][i] += Bs[p][j] * W[i][p];
        }
        double[][] BtWB = new double[s][];
        double[] BtWz = new double[s];
        for (int j = 0; j < s; j++)
        {
            BtWB[j] = new double[s];
            for (int t = 0; t < s; t++)
                for (int i = 0; i < dd; i++)
                    BtWB[j][t] += BtW[j][i] * Bs[i][t];
            for (int i = 0; i < dd; i++) BtWz[j] += BtW[j][i] * z[i];
        }

        int n = s + 1;
        double[][] kkt = new double[n][];
        double[] rhs = new double[n];
        for (int j = 0; j < s; j++)
        {
            kkt[j] = new double[n];
            Array.Copy(BtWB[j], kkt[j], s);
            kkt[j][s] = 1;
            rhs[j] = BtWz[j];
        }
        kkt[s] = new double[n];
        for (int j = 0; j < s; j++) kkt[s][j] = 1;
        kkt[s][s] = 0;
        rhs[s] = 1;

        if (!LinearAlgebra.Solve(kkt, rhs, out double[] sol))
        {
            f = Array.Empty<double>();
            fCov = Array.Empty<double[]>();
            return false;
        }
        f = sol.Take(s).ToArray();

        // 协方差：KKT 逆的左上角块，即 (B^TWB) 在零空间上的伪逆。
        try
        {
            double[][] kktInv = LinearAlgebra.Inverse(kkt);
            fCov = new double[s][];
            for (int i = 0; i < s; i++)
            {
                fCov[i] = new double[s];
                Array.Copy(kktInv[i], fCov[i], s);
            }
            // 非正对角元（如 dof=0 共线子集）视为不可用，置零并保留主结构。
            for (int i = 0; i < s; i++)
                if (fCov[i][i] < 0) fCov[i][i] = 0;
        }
        catch
        {
            fCov = new double[s][];
            for (int i = 0; i < s; i++) fCov[i] = new double[s];
        }
        return true;
    }

    private static double[][] ExpandCov(double[][] sub, int[] combo, int k)
    {
        double[][] full = new double[k][];
        for (int i = 0; i < k; i++)
        {
            full[i] = new double[k];
            for (int j = 0; j < k; j++)
            {
                int ai = Array.IndexOf(combo, i);
                int aj = Array.IndexOf(combo, j);
                if (ai >= 0 && aj >= 0) full[i][j] = sub[ai][aj];
            }
        }
        return full;
    }

    private static IEnumerable<int[]> Combinations(int n, int r)
    {
        int[] combo = Enumerable.Range(0, r).ToArray();
        while (true)
        {
            yield return (int[])combo.Clone();
            int i = r - 1;
            while (i >= 0 && combo[i] == n - r + i) i--;
            if (i < 0) yield break;
            combo[i]++;
            for (int j = i + 1; j < r; j++) combo[j] = combo[j - 1] + 1;
        }
    }

    private static string DeterministicId(string prefix, params object[] parts)
    {
        string joined = string.Join("|", parts.Select(p => p?.ToString() ?? ""));
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return prefix + "_" + Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}

public sealed partial class MixingSolver
{
    private List<FeasibleVertex> EnumerateVertices(
        MixingRequest req, double[][] B, double[] z, double[][] W)
    {
        int k = req.Sources.Count;
        int dd = B.Length;
        int maxSubset = Math.Min(k, dd + 1);
        var verts = new List<FeasibleVertex>();

        foreach (int s in Enumerable.Range(2, maxSubset - 1))
        {
            foreach (int[] combo in Combinations(k, s))
            {
                double[][] Bs = B.Select(row => combo.Select(j => row[j]).ToArray()).ToArray();
                if (!ConstrainedLeastSquares(Bs, z, W, out double[] f, out _)) continue;
                if (f.Any(v => v < -FractionEps)) continue;

                double[] fullF = new double[k];
                for (int t = 0; t < s; t++) fullF[combo[t]] = Math.Max(0, f[t]);
                double[] fitted = LinearAlgebra.MultiplyVec(B, fullF);
                double[] resid = z.Zip(fitted, (a, b) => a - b).ToArray();
                double[] wr = LinearAlgebra.MultiplyVec(W, resid);
                double chi2 = resid.Zip(wr, (a, b) => a * b).Sum();
                int dof = Math.Max(0, dd - s + 1);
                double p = ChiSquare.PValue(chi2, dof);
                double crit = ChiSquare.Critical95(Math.Max(1, dof));
                bool accepted = chi2 <= (dof == 0 ? 1e-8 : crit);

                verts.Add(new FeasibleVertex(
                    combo.Select(j => req.Sources[j].SourceId).ToList(),
                    fullF.ToList(), chi2, p, accepted,
                    accepted ? null : RuleCodes.OutsideEnvelope));
            }
        }
        return verts;
    }

    private static MixingStatus DecideStatus(
        int present, int total, bool rankDeficient, bool nearCollinear,
        bool outsideEnvelope, bool priorInfeasible, List<CandidateMix> candidates)
    {
        int accepted = candidates.Count(c => c.Accepted);
        if (priorInfeasible && accepted == 0) return MixingStatus.InfeasiblePrior;
        if (outsideEnvelope && accepted == 0) return MixingStatus.OutsideEnvelope;
        if (accepted >= 2) return MixingStatus.MultipleCandidates;
        if (rankDeficient) return MixingStatus.UnderIdentified;
        if (nearCollinear) return MixingStatus.UnderIdentified;
        return MixingStatus.Unique;
    }

    private static void AddConstraintJudgments(
        MixingRequest req, List<CandidateMix> candidates, List<MachineJudgment> judgments)
    {
        foreach (var c in candidates.Where(x => x.Accepted))
        {
            double sum = c.Fractions.Sum();
            double min = c.Fractions.Min();
            judgments.Add(new MachineJudgment(
                RuleCodes.SimplexConstraints, "info", true,
                $"{req.SampleId}/{c.CandidateId}",
                "比例满足和为一且非负；由等式约束 KKT 求得，无截断后归一化",
                new Dictionary<string, object?>
                {
                    ["sum"] = sum,
                    ["minFraction"] = min,
                    ["tolerance"] = FractionEps,
                    ["sources"] = string.Join(",", c.SourceIds)
                }));
        }
    }
}

/// <summary>机器可读取规则代码：固定案例与界面均据此断言，不靠自然语言匹配。</summary>
public static class RuleCodes
{
    public const string NearCollinear = "NEAR_COLLINEAR_ENDMEMBERS";
    public const string OutsideEnvelope = "SAMPLE_OUTSIDE_ENVELOPE";
    public const string CorrelatedError = "CORRELATED_ERROR_COVARIANCE";
    public const string MissingDimensions = "MISSING_ISOTOPE_DIMENSION";
    public const string FractionationSwitch = "FRACTIONATION_VERSION_SWITCH";
    public const string MultipleFeasibleRegions = "MULTIPLE_FEASIBLE_REGIONS";
    public const string InfeasiblePrior = "INFEASIBLE_PRIOR";
    public const string SimplexConstraints = "SIMPLEX_CONSTRAINTS_EXACT";
    public const string CovariancePropagation = "COVARIANCE_PROPAGATION";
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace IsotopeMix.Core.Solver;

// 输入：样品与端元在规范化空间的坐标、测量协方差、分馏偏移、先验。
public sealed class MixingInput
{
    public required string SampleId { get; init; }
    public required List<string> EndmemberIds { get; init; }
    public required double?[] SampleCoords { get; init; }
    public required List<double?[]> EndmemberCoords { get; init; }
    public double[,]? Covariance { get; init; }          // 样品测量协方差（规范单位）
    public bool CovarianceCorrelated { get; init; }
    public double[,]? FractionationOffsets { get; init; } // [tracer, endmember]
    public required List<string> Tracers { get; init; }
    public double[]? PriorLower { get; init; }
    public double[]? PriorUpper { get; init; }
    public bool EnforceSimplex { get; init; } = true;
    public double ConfidenceLevel { get; init; } = 0.95;
    public double CollinearityThreshold { get; init; } = 1e-2;
}

// 三端元稳定同位素混合可行域求解。
// 模型：s = E p + ε，p>=0、1'p=1（simplex 真实约束，不做截断后归一化）；
// ε ~ N(0, Σ)，相关误差按协方差传播；置信椭球 (s-Ep)' Σ^-1 (s-Ep) <= χ²。
public static class MixingSolver
{
    // 低维卡方临界值（常用置信水平）。
    static double ChiSquareQuantile(int df, double level) => (df, Math.Round(level, 4)) switch
    {
        (1, 0.95) => 3.84146,
        (2, 0.95) => 5.99146,
        (3, 0.95) => 7.81473,
        (1, 0.90) => 2.70554,
        (2, 0.90) => 4.60517,
        (3, 0.90) => 6.25139,
        _ => df * (1.0 - 2.0 / (9.0 * df) + 2.326 * Math.Sqrt(2.0 / (9.0 * df))) // Wilson 近似
    };

    public static FeasibilityResult Solve(MixingInput input)
    {
        var result = new FeasibilityResult
        {
            SampleId = input.SampleId,
            EndmemberIds = input.EndmemberIds.ToList(),
            ConfidenceLevel = input.ConfidenceLevel,
            CorrelatedErrorsUsed = input.CovarianceCorrelated
        };

        if (input.EndmemberIds.Count != 3)
        {
            result.Status = FeasibilityStatus.NotEvaluable;
            result.Identifiable = false;
            result.Findings.Add(new Finding
            {
                Code = "ENDMEMBER_COUNT_UNSUPPORTED", Severity = "error",
                Message = "该求解器显式处理三端元情形；其余情形登记但不做单点伪造。"
            });
            return result;
        }

        // 只取样品与全部端元共同有效的维（缺失维不填零）。
        var active = new List<int>();
        for (int t = 0; t < input.Tracers.Count; t++)
        {
            bool ok = input.SampleCoords[t].HasValue;
            for (int e = 0; e < 3 && ok; e++) ok &= input.EndmemberCoords[e][t].HasValue;
            if (ok) active.Add(t);
        }
        result.ActiveTracers = active.Select(i => input.Tracers[i]).ToList();
        int k = active.Count;

        if (k == 0)
        {
            result.Status = FeasibilityStatus.NotEvaluable;
            result.Identifiable = false;
            result.Findings.Add(new Finding
            {
                Code = "MISSING_ISOTOPE_DIMENSION", Severity = "error",
                Message = "样品与端元没有共同有效的同位素维，无法判定混合比例。"
            });
            return result;
        }

        double[] s = active.Select(t => input.SampleCoords[t]!.Value).ToArray();
        double[,] em = new double[k, 3];
        for (int i = 0; i < k; i++)
            for (int e = 0; e < 3; e++)
            {
                double val = input.EndmemberCoords[e][active[i]]!.Value;
                if (input.FractionationOffsets is not null)
                    val += input.FractionationOffsets[active[i], e];
                em[i, e] = val;
            }

        // ---- 可识别性：设计矩阵 E（以端元 3 为参考的差分）的 SVD ----
        double[,] design = new double[k, 2];
        for (int i = 0; i < k; i++)
        {
            design[i, 0] = em[i, 0] - em[i, 2];
            design[i, 1] = em[i, 1] - em[i, 2];
        }
        LinearAlgebra.Svd(design, out var sv, out _);
        double cond = sv[0] > 1e-12 ? sv[Math.Min(1, sv.Length - 1)] / sv[0] : 0;
        bool nearCollinear = cond < input.CollinearityThreshold;
        if (nearCollinear)
        {
            result.Identifiable = false;
            result.Findings.Add(new Finding
            {
                Code = "ENDMEMBER_NEAR_COLLINEAR", Severity = "warning",
                Message = "端元在同位素空间近共线：混合比例不可唯一识别，仅给出合法可行域。",
                Evidence = new Dictionary<string, object>
                {
                    ["singularValues"] = sv,
                    ["conditionRatio"] = cond,
                    ["threshold"] = input.CollinearityThreshold
                }
            });
        }

        // ---- 协方差传播：相关误差必须使用完整 Σ，而非仅对角方差 ----
        double[,] sigma;
        bool correlated = input.CovarianceCorrelated;
        if (input.Covariance is null)
        {
            sigma = new double[k, k];
            for (int i = 0; i < k; i++) sigma[i, i] = 1.0;
            result.Findings.Add(new Finding
            {
                Code = "COVARIANCE_ASSUMED_UNIT", Severity = "info",
                Message = "未提供测量协方差，按单位方差近似（相关误差未声明）。"
            });
        }
        else
        {
            sigma = SubsetCovariance(input.Covariance, active);
            for (int i = 0; i < k; i++)
                if (sigma[i, i] <= 0) sigma[i, i] = 1e-8;
        }
        if (correlated)
        {
            result.Findings.Add(new Finding
            {
                Code = "CORRELATED_ERROR_PROPAGATED", Severity = "info",
                Message = "检测到非零协方差项，按完整协方差矩阵传播误差构造置信椭球。",
                Evidence = new Dictionary<string, object> { ["correlated"] = true }
            });
        }
        result.CovarianceUsed = sigma;

        return BuildRegion(input, result, s, em, sigma, k);
    }

    static double[,] SubsetCovariance(double[,] cov, List<int> active)
    {
        int k = active.Count;
        var sub = new double[k, k];
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++)
                sub[i, j] = cov[active[i], active[j]];
        return sub;
    }

    static FeasibilityResult BuildRegion(MixingInput input, FeasibilityResult result,
                                         double[] s, double[,] em, double[,] sigma, int k)
    {
        double kappa = ChiSquareQuantile(k, input.ConfidenceLevel);

        // r(p) = r0 - D p；r0 = s - e3，D 的列为 (e1-e3),(e2-e3)。
        double[] r0 = new double[k];
        double[,] d = new double[k, 2];
        for (int i = 0; i < k; i++)
        {
            r0[i] = s[i] - em[i, 2];
            d[i, 0] = em[i, 0] - em[i, 2];
            d[i, 1] = em[i, 1] - em[i, 2];
        }

        double[,] invSigma;
        try { invSigma = LinearAlgebra.InvertSymmetric(sigma); }
        catch (InvalidOperationException)
        {
            result.Status = FeasibilityStatus.NotEvaluable;
            result.Findings.Add(new Finding { Code = "SINGULAR_COVARIANCE", Severity = "error",
                Message = "测量协方差奇异，无法按协方差传播构造置信域。" });
            return result;
        }

        // Q = D' Σ^-1 D (2x2)，l = D' Σ^-1 r0，c = r0' Σ^-1 r0
        double[,] dtinv = new double[2, k];
        for (int a = 0; a < 2; a++)
            for (int i = 0; i < k; i++)
            {
                double sum = 0;
                for (int j = 0; j < k; j++) sum += d[j, a] * invSigma[i, j];
                dtinv[a, i] = sum;
            }
        double[,] q = new double[2, 2];
        double[] l = new double[2];
        double c = 0;
        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                double sum = 0;
                for (int i = 0; i < k; i++) sum += dtinv[a, i] * d[i, b];
                q[a, b] = sum;
            }
            double ls = 0, cs = 0;
            for (int i = 0; i < k; i++)
            {
                ls += dtinv[a, i] * r0[i];
                double ir = 0;
                for (int j = 0; j < k; j++) ir += invSigma[i, j] * r0[j];
                cs += r0[i] * ir;
            }
            l[a] = ls;
            c = cs;
        }

        // 椭球中心 p* = Q^-1 l（未施加约束的 GLS 解；仅作参照，绝不截断归一化）。
        double[]? gls = null;
        double detQ = q[0, 0] * q[1, 1] - q[0, 1] * q[1, 0];
        if (Math.Abs(detQ) > 1e-14)
            gls = LinearAlgebra.SolveLinear2(q[0, 0], q[0, 1], q[1, 0], q[1, 1], l[0], l[1]);

        // 置信椭球多边形化（以 p* 为中心；Q 半正定，对称化后特征分解）。
        q[0, 1] = q[1, 0] = (q[0, 1] + q[1, 0]) / 2.0;
        List<double[]> region = EllipsePolygon(q, l, c, kappa, gls, result);

        // simplex：真实的非负与和为一约束（p1>=0, p2>=0, p1+p2<=1）。
        region = Polygon2.Clip(region, -1, 0, 0);     // p1 >= 0
        region = Polygon2.Clip(region, 0, -1, 0);     // p2 >= 0
        region = Polygon2.Clip(region, 1, 1, 1);      // p1 + p2 <= 1

        bool emptyAfterSimplex = region.Count == 0;
        bool priorClashed = false;
        // 先验：箱型界限仍以半平面裁剪；先验冲突单独标记（不可行先验）。
        for (int e = 0; e < 3; e++)
        {
            double lo = input.PriorLower?[e] ?? 0;
            double up = input.PriorUpper?[e] ?? 1;
            if (lo > up + 1e-12 || lo < -1e-12 || up > 1 + 1e-12)
            {
                priorClashed = true;
                result.Findings.Add(new Finding
                {
                    Code = "PRIOR_OUTSIDE_SIMPLEX", Severity = "error",
                    Message = $"端元 {input.EndmemberIds[e]} 的先验界限 [{lo},{up}] 超出 simplex，属不可行先验。"
                });
            }
            if (e == 0) region = Polygon2.Clip(region, -1, 0, -lo);
            if (e == 1) region = Polygon2.Clip(region, 0, -1, -lo);
            if (e == 2) region = Polygon2.Clip(region, 1, 1, 1 - lo);
            if (e == 0) region = Polygon2.Clip(region, 1, 0, up);
            if (e == 1) region = Polygon2.Clip(region, 0, 1, up);
            if (e == 2) region = Polygon2.Clip(region, -1, -1, -(1 - up));
        }

        region = Polygon2.SortByAngle(region);
        result.PolygonVertices = region;

        // 超包络：未约束 GLS 解若在 simplex 外，或椭球∩simplex 为空，则样品超端元包络。
        bool glsOutside = gls is null || gls[0] < -1e-8 || gls[1] < -1e-8 || gls[0] + gls[1] > 1 + 1e-8;
        if (emptyAfterSimplex)
        {
            result.WithinEnvelope = false;
            result.Status = FeasibilityStatus.Infeasible;
            result.Findings.Add(new Finding
            {
                Code = "SAMPLE_OUTSIDE_ENVELOPE", Severity = "warning",
                Message = "在给定置信水平下样品落于端元包络之外，simplex 内无可行混合比例。",
                Evidence = new Dictionary<string, object>
                {
                    ["glsProportions"] = gls ?? new double[] { double.NaN, double.NaN },
                    ["confidenceLevel"] = input.ConfidenceLevel
                }
            });
        }

        if (region.Count == 0)
        {
            if (!emptyAfterSimplex)
            {
                result.Status = FeasibilityStatus.Infeasible;
                result.Findings.Add(new Finding
                {
                    Code = priorClashed ? "INFEASIBLE_PRIOR" : "NO_FEASIBLE_REGION",
                    Severity = "error",
                    Message = priorClashed
                        ? "先验与置信椭球及 simplex 不相交：不可行先验，未产生任何比例。"
                        : "置信椭球、simplex 与先验的交集为空。"
                });
            }
            AttachResiduals(result, s, em, d, r0, invSigma, gls, k, kappa, glsOutside);
            return result;
        }

        var centroid = Polygon2.Centroid(region);
        result.RepresentativeProportions = new double?[] { centroid[0], centroid[1], 1 - centroid[0] - centroid[1] };
        result.CandidateProportions = BuildCandidates(region);
        result.Status = region.Count == 1 ? FeasibilityStatus.Unique : FeasibilityStatus.Feasible;
        if (region.Count > 1 && !result.Identifiable)
        {
            result.Findings.Add(new Finding
            {
                Code = "MULTIPLE_LEGAL_CANDIDATES", Severity = "info",
                Message = $"保留 {result.CandidateProportions.Count} 个合法候选，不选择单点答案。",
                Evidence = new Dictionary<string, object> { ["candidateCount"] = result.CandidateProportions.Count }
            });
        }
        AttachResiduals(result, s, em, d, r0, invSigma,
            new[] { centroid[0], centroid[1] }, k, kappa, glsOutside);
        return result;
    }

    // 残差与 Mahalanobis 距离按完整协方差计算（相关误差传播的直接体现）。
    static void AttachResiduals(FeasibilityResult result, double[] s, double[,] em,
                                double[,] d, double[] r0, double[,] invSigma,
                                double[]? p, int k, double kappa, bool glsOutside)
    {
        double[] pp = p ?? new[] { 0.0, 0.0 };
        double[] residual = new double[k];
        for (int i = 0; i < k; i++)
            residual[i] = r0[i] - d[i, 0] * pp[0] - d[i, 1] * pp[1];
        result.ResidualVector = residual;
        double m2 = 0;
        for (int i = 0; i < k; i++)
        {
            double row = 0;
            for (int j = 0; j < k; j++) row += invSigma[i, j] * residual[j];
            m2 += residual[i] * row;
        }
        result.MahalanobisSquared = m2;
        if (glsOutside)
        {
            result.EnvelopeMahalanobisSquared = m2;
            result.Findings.Add(new Finding
            {
                Code = "MAHALANOBIS_EXCEEDS_CRITICAL", Severity = "warning",
                Message = $"simplex 上最小 Mahalanobis²={m2:G4} 超过临界值 {kappa:G4}。",
                Evidence = new Dictionary<string, object> { ["mahalanobisSquared"] = m2, ["critical"] = kappa }
            });
        }
    }

    static List<double[]> BuildCandidates(List<double[]> poly)
    {
        // 候选 = 多边形顶点 + 重心；全部合法，显式保留（不截断、不归一化）。
        var cands = poly.Select(p => new[] { p[0], p[1], 1 - p[0] - p[1] }).ToList();
        if (poly.Count >= 3)
        {
            var c = Polygon2.Centroid(poly);
            cands.Add(new[] { c[0], c[1], 1 - c[0] - c[1] });
        }
        return cands;
    }

    // 构造 (p-p*)' Q (p-p*) <= kappa' 的多边形（kappa' = kappa - c + l'p*）。
    // Q 秩 2：椭圆；秩 1：条带（缺一个同位素信息维）；秩 0：整个平面。
    static List<double[]> EllipsePolygon(double[,] q, double[] l, double c,
                                         double kappa, double[]? center,
                                         FeasibilityResult result)
    {
        double detQ = q[0, 0] * q[1, 1] - q[0, 1] * q[1, 0];
        double trace = q[0, 0] + q[1, 1];
        double disc = Math.Max(0, trace * trace / 4.0 - detQ);
        double lam1 = trace / 2.0 + Math.Sqrt(disc);
        double lam2 = trace / 2.0 - Math.Sqrt(disc);
        const double eps = 1e-10;

        if (lam2 > eps && center is not null)
        {
            double kp = kappa - c + l[0] * center[0] + l[1] * center[1];
            if (kp < 0) return new List<double[]>();
            // 特征向量（对应 lam1）
            double vx = 1, vy = 0;
            if (Math.Abs(q[0, 1]) > eps)
            {
                vx = lam1 - q[1, 1];
                vy = q[1, 0];
                double norm = Math.Sqrt(vx * vx + vy * vy);
                vx /= norm; vy /= norm;
            }
            double a = Math.Sqrt(kp / lam1);
            double b = Math.Sqrt(kp / lam2);
            const int n = 72;
            var pts = new List<double[]>(n);
            for (int i = 0; i < n; i++)
            {
                double ang = 2 * Math.PI * i / n;
                double ex = a * Math.Cos(ang);
                double ey = b * Math.Sin(ang);
                pts.Add(new[]
                {
                    center[0] + vx * ex - vy * ey,
                    center[1] + vy * ex + vx * ey
                });
            }
            return pts;
        }

        if (lam1 > eps && center is not null)
        {
            // 秩 1：沿 lam1 方向有界，垂直方向无界 → 用一个大框表达条带，
            // 稍后与 simplex 相交。缺失一个有效同位素维即落入此情形。
            result.Identifiable = false;
            if (!result.Findings.Any(f => f.Code == "MISSING_ISOTOPE_DIMENSION"))
                result.Findings.Add(new Finding
                {
                    Code = "RANK_DEFICIENT_INFORMATION", Severity = "warning",
                    Message = "同位素信息秩为 1：可行集为条带，比例不可唯一识别。"
                });
            double kp = kappa - c + l[0] * center[0] + l[1] * center[1];
            if (kp < 0) return new List<double[]>();
            double vx = 1, vy = 0;
            if (Math.Abs(q[0, 1]) > eps || Math.Abs(q[1, 0]) > eps)
            {
                vx = lam1 - q[1, 1];
                vy = q[1, 0];
                double norm = Math.Sqrt(vx * vx + vy * vy) is 0 ? 1 : Math.Sqrt(vx * vx + vy * vy);
                vx /= norm; vy /= norm;
            }
            double a = Math.Sqrt(kp / lam1);
            double px = -vy, py = vx;
            const double big = 50;
            return new List<double[]>
            {
                new[] { center[0] - vx * a + px * big, center[1] - vy * a + py * big },
                new[] { center[0] + vx * a + px * big, center[1] + vy * a + py * big },
                new[] { center[0] + vx * a - px * big, center[1] - vy * a - py * big },
                new[] { center[0] - vx * a - px * big, center[1] - vy * a - py * big }
            };
        }

        // 秩 0：完全无信息（端元完全重合）。
        result.Identifiable = false;
        result.Findings.Add(new Finding
        {
            Code = "NO_ISOTOPIC_INFORMATION", Severity = "error",
            Message = "端元在有效维上完全重合，无法约束混合比例。"
        });
        const double big0 = 50;
        return new List<double[]>
        {
            new[] { -big0, -big0 }, new[] { big0, -big0 },
            new[] { big0, big0 }, new[] { -big0, big0 }
        };
    }
}

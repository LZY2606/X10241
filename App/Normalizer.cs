using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IsoMix.Cases;
using IsoMix.Domain;

namespace IsoMix.App;

/// <summary>
/// 派生层规范化：原始记录始终保留，这里只生成数值输入。
/// δ 值单位在案例内一致（per_mil_*），故同基准则直接取数；无法解析的值保留在原位并附诊断。
/// </summary>
public static class Normalizer
{
    public static double? ParseDouble(string raw, out string? diagnostic)
    {
        diagnostic = null;
        if (string.IsNullOrWhiteSpace(raw)) { diagnostic = "空值：无法转换，已留在原位。"; return null; }
        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
        diagnostic = $"无法把原始表示 “{raw}” 解析为数：保留原始文本，未参与计算或绘图。";
        return null;
    }

    public sealed class Prepared
    {
        public MixingInput Input { get; init; } = new();
        public List<Diagnostic> Diagnostics { get; init; } = new();
        public List<string> UsedAxes { get; init; } = new();
    }

    public static Prepared Prepare(
        SampleSpec sample,
        EndmemberSetSpec endmemberSet,
        CovarianceSpec? covariance,
        FractionationSpec fractionation,
        PriorSpec? prior,
        List<AxisSpec> allAxes,
        IReadOnlyList<string> extraMissingAxes,
        bool multiRegionDemo)
    {
        var diagnostics = new List<Diagnostic>();
        var missing = new HashSet<string>(sample.MissingAxes.Concat(extraMissingAxes));

        // 选定参与计算的轴：至少要有该轴样品值可解析、端元齐备，且未声明缺失。
        var usedAxes = new List<string>();
        var unparseableAxes = new List<string>();
        foreach (var axis in allAxes)
        {
            bool sampleHas = sample.Values.TryGetValue(axis.Key, out var rv);
            if (missing.Contains(axis.Key) || !sampleHas)
            {
                if (missing.Contains(axis.Key) || !sampleHas)
                    diagnostics.Add(new Diagnostic
                    {
                        Code = MixingMath.IdMissingAxes,
                        Severity = Severity.Info,
                        Message = $"同位素维 {axis.Label} 缺失：已降维处理，未用 0 或均值填补。",
                        AffectedObjectIds = new() { sample.Id, axis.Key }
                    });
                continue;
            }
            double? sv = ParseDouble(rv!.Raw, out var diag);
            bool endOk = endmemberSet.Members.All(m => m.Composition.ContainsKey(axis.Key));
            if (sv is null)
            {
                unparseableAxes.Add(axis.Key);
                diagnostics.Add(new Diagnostic
                {
                    Code = "DIMENSIONS.UNPARSEABLE_VALUE",
                    Severity = Severity.Warning,
                    Message = $"{sample.Name} 在 {axis.Label} 上：{diag}",
                    AffectedObjectIds = new() { sample.Id }
                });
                continue;
            }
            if (!endOk)
            {
                diagnostics.Add(new Diagnostic
                {
                    Code = "DIMENSIONS.ENDMEMBER_MISSING_AXIS",
                    Severity = Severity.Warning,
                    Message = $"端元集合在 {axis.Label} 上不完整，该维不参与反演。",
                    AffectedObjectIds = new() { endmemberSet.Id }
                });
                continue;
            }
            usedAxes.Add(axis.Key);
        }

        int d = usedAxes.Count;
        int k = endmemberSet.Members.Count;
        var y = new double[d];
        var G = new double[d, k];
        var ids = endmemberSet.Members.Select(m => m.Id).ToList();
        var names = endmemberSet.Members.Select(m => m.Name).ToList();

        for (int a = 0; a < d; a++)
        {
            var axisKey = usedAxes[a];
            y[a] = ParseDouble(sample.Values[axisKey].Raw, out _)!.Value;
            for (int m = 0; m < k; m++)
            {
                double comp = ParseDouble(endmemberSet.Members[m].Composition[axisKey].Raw, out _) ?? 0;
                double shift = FractionationShift(fractionation, axisKey, endmemberSet.Members[m].Id, diagnostics);
                G[a, m] = comp + shift;
            }
        }

        double[,]? sigma = null;
        if (covariance is not null && d >= 1)
        {
            sigma = BuildCovariance(covariance, usedAxes);
        }

        var input = new MixingInput
        {
            Axes = usedAxes,
            Observation = y,
            EndmemberMatrix = G,
            EndmemberIds = ids,
            EndmemberNames = names,
            MeasurementCovariance = sigma,
            MultiRegionDemonstration = multiRegionDemo,
            IdentifiabilityThreshold = 1e-3
        };

        if (prior is not null)
        {
            input.PriorLo = new double[k];
            input.PriorHi = new double[k];
            for (int m = 0; m < k; m++)
            {
                input.PriorLo[m] = ParseDouble(prior.Lo[ids[m]], out _) ?? 0;
                input.PriorHi[m] = ParseDouble(prior.Hi[ids[m]], out _) ?? 1;
            }
        }

        return new Prepared { Input = input, Diagnostics = diagnostics, UsedAxes = usedAxes };
    }

    private static double FractionationShift(FractionationSpec frac, string axis, string endmemberId, List<Diagnostic> diag)
    {
        double total = 0;
        foreach (var f in frac.Factors.Where(f => f.Axis == axis))
        {
            if (f.Endmember is not null && f.Endmember != endmemberId) continue;
            double? v = ParseDouble(f.Delta, out var dmsg);
            if (v is null)
            {
                diag.Add(new Diagnostic { Code = "FRACTIONATION.UNPARSEABLE", Severity = Severity.Warning, Message = $"分馏因子无法解析：{dmsg}" });
                continue;
            }
            total += v.Value;
        }
        return total;
    }

    private static double[,] BuildCovariance(CovarianceSpec spec, List<string> usedAxes)
    {
        var idx = spec.AxisOrder.Select((a, i) => (a, i)).ToDictionary(t => t.a, t => t.i);
        int d = usedAxes.Count;
        var m = new double[d, d];
        for (int i = 0; i < d; i++)
            for (int j = 0; j < d; j++)
            {
                int ri = idx[usedAxes[i]], rj = idx[usedAxes[j]];
                double? v = ParseDouble(spec.Matrix[ri][rj], out _);
                m[i, j] = v ?? 0;
            }
        return m;
    }
}

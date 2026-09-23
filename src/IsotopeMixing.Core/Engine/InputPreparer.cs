using System.Text.Json;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Engine;

/// <summary>把原始记录装配成规范化分析输入。所有换算失败只发诊断, 不丢弃原值。</summary>
public sealed class InputPreparer
{
    public PreparedInput Prepare(AnalysisInput input)
    {
        var diags = new List<Diagnostic>();
        var sample = input.SampleRecord.Content.Deserialize<SampleContent>(Json.Options)
                     ?? throw new InvalidOperationException("样品记录内容无效");
        var frac = input.FractionationRecord.Content.Deserialize<FractionationContent>(Json.Options)
                   ?? throw new InvalidOperationException("分馏版本记录内容无效");

        var dimRecords = input.DimensionRecords
            .Select(r => r.Content.Deserialize<DimensionContent>(Json.Options)!)
            .Where(d => d is not null).ToList();
        var allDims = dimRecords.Select(d => d.Key).ToList();
        var labels = dimRecords.ToDictionary(d => d.Key, d => $"{d.Label} ({d.Unit} {d.Datum})");

        var missing = new List<string>();
        var unconvertible = new List<string>();
        var usable = new List<string>();
        var normalizedSampleValues = new Dictionary<string, double>();
        foreach (var dim in allDims)
        {
            if (!sample.Values.TryGetValue(dim, out var q) || q is null || q.OriginalText is null && q.Value is null)
            {
                missing.Add(dim);
                diags.Add(new Diagnostic("missing-dimension", "warning",
                    $"样品 {sample.Code} 缺少同位素维 {dim}，该维不进入计算", new[] { input.SampleRecord.Id, dim }));
                continue;
            }
            var normalized = NormalizeQuantity(q, diags, input.SampleRecord.Id, dim);
            if (normalized is null) { unconvertible.Add(dim); continue; }
            normalizedSampleValues[dim] = normalized.Value;
            usable.Add(dim);
        }

        // 分馏校正: x_corrected = x_sample - offset(dim)
        var offset = new double[usable.Count];
        for (int i = 0; i < usable.Count; i++)
        {
            if (frac.Offsets.TryGetValue(usable[i], out var fq) && fq?.Value.HasValue == true)
                offset[i] = UnitConverter.Normalize(fq).Value ?? 0;
        }
        var sampleVector = usable.Select((d, i) => normalizedSampleValues[d] - offset[i]).ToArray();

        var sampleCov = BuildSampleCovariance(input, usable, sample, diags);
        bool correlated = HasOffDiagonal(sampleCov);

        var endmembers = new List<PreparedEndmember>();
        foreach (var rec in input.EndmemberRecords)
        {
            var ec = rec.Content.Deserialize<EndmemberContent>(Json.Options)!;
            var means = new List<double[]>();
            var modeIds = new List<string>();
            foreach (var mode in ec.Modes)
            {
                var vec = new double[usable.Count];
                for (int i = 0; i < usable.Count; i++)
                {
                    if (!mode.Means.TryGetValue(usable[i], out var mq) || mq is null)
                    { vec[i] = double.NaN; continue; }
                    var n = NormalizeQuantity(mq, diags, rec.Id, usable[i]);
                    vec[i] = n ?? double.NaN;
                }
                means.Add(vec);
                modeIds.Add(mode.ModeId);
            }
            double[][]? ecov = null;
            if (!string.IsNullOrEmpty(ec.CovarianceRef))
            {
                var covRec = input.CovarianceRecords.FirstOrDefault(r =>
                    r.Content.Deserialize<CovarianceContent>(Json.Options)?.Key == ec.CovarianceRef);
                if (covRec is not null)
                    ecov = ExtractCovariance(covRec, usable, ec.Key, diags, out _);
            }
            endmembers.Add(new PreparedEndmember
            {
                Key = ec.Key, Name = ec.Name, Record = rec,
                ModeIndices = Enumerable.Range(0, ec.Modes.Count).ToList(),
                ModeMeans = means, ModeIds = modeIds, Covariance = ecov
            });
        }

        PreparedPrior? prior = null;
        if (input.PriorRecord is not null)
        {
            var pc = input.PriorRecord.Content.Deserialize<PriorContent>(Json.Options)!;
            var lower = new double[endmembers.Count];
            var upper = new double[endmembers.Count];
            bool consistent = true;
            for (int i = 0; i < endmembers.Count; i++)
            {
                if (pc.EndmemberBounds.TryGetValue(endmembers[i].Key, out var b) && b is not null)
                {
                    lower[i] = System.Math.Clamp(b.Min, 0, 1);
                    upper[i] = System.Math.Clamp(b.Max, 0, 1);
                    if (b.Min > b.Max + 1e-12 || b.Min < 0 || b.Max > 1) consistent = false;
                }
                else { lower[i] = 0; upper[i] = 1; }
            }
            if (lower.Sum() > 1 + 1e-9 || upper.Sum() < 1 - 1e-9) consistent = false;
            prior = new PreparedPrior { Lower = lower, Upper = upper, SelfConsistent = consistent };
        }

        return new PreparedInput
        {
            Source = input,
            Dimensions = usable,
            DimensionLabels = labels,
            Sample = new PreparedSample
            {
                Content = sample, Dimensions = usable, Missing = missing, Unconvertible = unconvertible,
                Vector = sampleVector, Covariance = sampleCov,
                Units = usable.ToDictionary(d => d, d => "permille")
            },
            Endmembers = endmembers,
            FractionationOffset = offset,
            Fractionation = frac,
            Prior = prior,
            Diagnostics = diags
        };
    }

    private static double? NormalizeQuantity(Quantity q, List<Diagnostic> diags, string recordId, string dim)
    {
        if (q.Value.HasValue && UnitConverter.CanConvert(q.Unit, "permille"))
            return UnitConverter.Normalize(q).Value!.Value;
        if (q.Value.HasValue)
        {
            diags.Add(new Diagnostic("unconvertible-unit", "error",
                $"记录 {recordId} 的维 {dim} 单位 '{q.Unit}' 无法换算，原值 '{q.OriginalText ?? q.Unit}' 保留在原始记录中",
                new[] { recordId, dim }));
            return null;
        }
        diags.Add(new Diagnostic("unparseable-value", "error",
            $"记录 {recordId} 的维 {dim} 值 '{q.OriginalText}' 无法解析，保留原位置", new[] { recordId, dim }));
        return null;
    }

    private static double[][] BuildSampleCovariance(AnalysisInput input, List<string> dims, SampleContent sample, List<Diagnostic> diags)
    {
        CovarianceContent? cov = null;
        if (!string.IsNullOrEmpty(sample.CovarianceRef))
        {
            cov = input.CovarianceRecords
                .Select(r => r.Content.Deserialize<CovarianceContent>(Json.Options))
                .FirstOrDefault(c => c?.Key == sample.CovarianceRef);
        }
        cov ??= input.CovarianceRecords
            .Select(r => r.Content.Deserialize<CovarianceContent>(Json.Options))
            .FirstOrDefault(c => c is not null && c.Scope == "sample" && c.Target == sample.Code);

        if (cov is null)
        {
            diags.Add(new Diagnostic("covariance-missing", "info",
                $"样品 {sample.Code} 无测量协方差记录，按独立 1σ=0.15‰ 处理", new[] { input.SampleRecord.Id }));
            var eye = LinearAlgebra.Identity(dims.Count);
            for (int i = 0; i < dims.Count; i++) eye[i][i] = 0.15 * 0.15;
            return eye;
        }
        return ExtractCovarianceFromContent(cov, dims, input.SampleRecord.Id, diags);
    }

    private static double[][] ExtractCovariance(RawRecord rec, List<string> dims, string target,
        List<Diagnostic> diags, out bool correlated)
    {
        var cov = rec.Content.Deserialize<CovarianceContent>(Json.Options)!;
        correlated = false;
        return ExtractCovarianceFromContent(cov, dims, rec.Id, diags);
    }

    private static double[][] ExtractCovarianceFromContent(CovarianceContent cov, List<string> dims,
        string recordId, List<Diagnostic> diags)
    {
        int n = dims.Count;
        var m = LinearAlgebra.Identity(n);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                int ii = cov.Dimensions.IndexOf(dims[i]);
                int jj = cov.Dimensions.IndexOf(dims[j]);
                if (ii >= 0 && jj >= 0) m[i][j] = cov.Matrix[ii][jj];
                else if (i == j) { m[i][j] = 0.15 * 0.15; diags.Add(new Diagnostic("covariance-assumed",
                    "info", $"协方差 {cov.Key} 缺少维 {dims[i]}，方差取默认值", new[] { recordId, dims[i] })); }
            }
        // 对称化与 jitter 保证半正定
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double avg = (m[i][j] + m[j][i]) / 2;
                m[i][j] = m[j][i] = avg;
                if (System.Math.Abs(avg) > 1e-15)
                    diags.Add(new Diagnostic("correlated-error", "info",
                        $"维 {dims[i]}–{dims[j]} 存在相关误差(协方差 {avg:G4})，已按协方差传播",
                        new[] { recordId, dims[i], dims[j] }));
            }
        for (int i = 0; i < n; i++) if (m[i][i] <= 0) m[i][i] = 1e-6;
        return m;
    }

    private static bool HasOffDiagonal(double[][] m)
    {
        for (int i = 0; i < m.Length; i++)
            for (int j = i + 1; j < m.Length; j++)
                if (System.Math.Abs(m[i][j]) > 1e-15) return true;
        return false;
    }
}

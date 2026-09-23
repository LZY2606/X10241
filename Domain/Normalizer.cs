namespace IsotopeMix.Domain;

/// <summary>派生层：把原始记录规范化到 δ per mil 与统一坐标基准。无法转换的值保留诊断并标记缺失。</summary>
public static class Normalizer
{
    public static NormalizedSample Normalize(SampleRecord s, IReadOnlyList<DimensionDef> dims)
    {
        var d = dims.Count;
        var vec = new double[d];
        var present = new bool[d];
        var diag = new List<string>();
        for (var i = 0; i < d; i++)
        {
            var dv = i < s.Values.Count ? s.Values[i] : new DimValue(null, "", UnitKind.Unknown, dims[i].Scale, "missing-value");
            if (!dv.Convertible)
            {
                present[i] = false;
                vec[i] = double.NaN;
                diag.Add($"sample:{s.Id} dim:{dims[i].Key} kept-raw:{dv.RawText} reason:{dv.Diagnostic ?? "not-convertible"}");
                continue;
            }
            if (!ScaleEquals(dv.Scale, dims[i].Scale))
            {
                present[i] = false; vec[i] = double.NaN;
                diag.Add($"sample:{s.Id} dim:{dims[i].Key} scale-mismatch:{dv.Scale}!={dims[i].Scale} kept-raw:{dv.RawText}");
                continue;
            }
            vec[i] = dv.ToPermil()!.Value;
            present[i] = true;
        }
        var (cov, cdiag) = NormalizeCovariance(s.Covariance, d, present, $"sample:{s.Id}");
        diag.AddRange(cdiag);
        return new NormalizedSample(s.Id, s.MaterialName, s.BranchId, s.PredecessorId, vec, present, cov, diag);
    }

    public static NormalizedEndmember Normalize(EndmemberRecord e, IReadOnlyList<DimensionDef> dims)
    {
        var d = dims.Count;
        var means = new double[d];
        var present = new bool[d];
        var diag = new List<string>();
        for (var i = 0; i < d; i++)
        {
            var dv = i < e.Means.Count ? e.Means[i] : new DimValue(null, "", UnitKind.Unknown, dims[i].Scale, "missing-value");
            if (!dv.Convertible || !ScaleEquals(dv.Scale, dims[i].Scale))
            {
                present[i] = false; means[i] = double.NaN;
                diag.Add($"endmember:{e.Id} dim:{dims[i].Key} kept-raw:{dv.RawText} reason:{dv.Diagnostic ?? (dv.Convertible ? "scale-mismatch" : "not-convertible")}");
                continue;
            }
            means[i] = dv.ToPermil()!.Value;
            present[i] = true;
        }
        var (cov, cdiag) = NormalizeCovariance(e.Covariance, d, present, $"endmember:{e.Id}");
        diag.AddRange(cdiag);
        return new NormalizedEndmember(e.Id, e.Name, e.MaterialName, e.BranchId, e.PredecessorId, means, present, cov, diag);
    }

    public static NormalizedFractionation Normalize(FractionationVersion f,
        IReadOnlyList<string> endmemberIds, IReadOnlyList<DimensionDef> dims)
    {
        var shift = new double[endmemberIds.Count, dims.Count];
        var present = new bool[endmemberIds.Count, dims.Count];
        var diag = new List<string>();
        for (var r = 0; r < endmemberIds.Count; r++)
        for (var c = 0; c < dims.Count; c++)
        {
            shift[r, c] = 0;
            present[r, c] = false;
            if (f.ByEndmember.TryGetValue(endmemberIds[r], out var list) && c < list.Count)
            {
                var dv = list[c].Mean;
                if (dv.Convertible && ScaleEquals(dv.Scale, dims[c].Scale))
                {
                    shift[r, c] = dv.ToPermil()!.Value;
                    present[r, c] = true;
                    continue;
                }
                diag.Add($"fractionation:{f.Id} endmember:{endmemberIds[r]} dim:{dims[c].Key} kept-raw:{dv.RawText}");
            }
            else
                diag.Add($"fractionation:{f.Id} endmember:{endmemberIds[r]} dim:{dims[c].Key} missing (shift=0 flagged)");
        }
        return new NormalizedFractionation(f.Id, f.Name, f.PredecessorId, shift, present, diag);
    }

    public static NormalizedPrior Normalize(PriorSpec p, IReadOnlyList<string> endmemberIds)
    {
        var diag = new List<string>();
        double[]? alpha = null;
        if (p.DirichletAlpha is not null)
        {
            alpha = new double[endmemberIds.Count];
            for (var i = 0; i < endmemberIds.Count; i++)
            {
                if (!p.DirichletAlpha.TryGetValue(endmemberIds[i], out var a) || a <= 0)
                {
                    diag.Add($"prior:{p.Id} endmember:{endmemberIds[i]} alpha missing/nonpositive");
                    alpha[i] = double.NaN;
                }
                else alpha[i] = a;
            }
        }
        var lo = p.LowerBound ?? 0.0;
        var hi = p.UpperBound ?? 1.0;
        if (lo < 0 || hi > 1 || lo > hi || lo * endmemberIds.Count > 1 || hi * endmemberIds.Count < 1)
            diag.Add($"prior:{p.Id} hard bounds infeasible: lo={lo} hi={hi} n={endmemberIds.Count}");
        return new NormalizedPrior(p.Id, p.Name, p.PredecessorId, alpha, lo, hi, diag);
    }

    private static bool ScaleEquals(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        (a.Contains("VPDB") && b.Contains("VPDB")) ||
        (a.Contains("AIR") && b.Contains("AIR"));

    /// <summary>
    /// 相关误差按协方差传播：保留完整协方差矩阵，不坍缩成独立误差。
    /// 单位统一为 permil²（若原始为 fraction 协方差则乘 1e6——这里所有协方差按 permil² 输入，
    /// 与 δ(‰) 匹配；无法确认时附诊断）。
    /// </summary>
    private static (double[,]?, List<string>) NormalizeCovariance(
        IReadOnlyList<IReadOnlyList<double?>>? raw, int d, bool[] present, string owner)
    {
        var diag = new List<string>();
        if (raw is null) return (null, new List<string> { $"{owner} covariance absent (assumed default with diagnostic)" });
        var cov = new double[d, d];
        for (var i = 0; i < d; i++)
        for (var j = 0; j < d; j++)
        {
            cov[i, j] = 0;
            if (i < raw.Count && j < raw[i].Count && raw[i][j] is { } v)
            {
                if (double.IsNaN(v) || double.IsInfinity(v) || (i == j && v < 0))
                    diag.Add($"{owner} cov[{i},{j}] invalid kept zero");
                else cov[i, j] = v;
            }
        }
        // 对称性检查：相关误差必须以对称正定矩阵传播
        for (var i = 0; i < d; i++)
        for (var j = i + 1; j < d; j++)
            if (Math.Abs(cov[i, j] - cov[j, i]) > 1e-12)
                diag.Add($"{owner} covariance asymmetric at [{i},{j}] symmetrized");
        // 缺失维度不参与似然
        for (var i = 0; i < d; i++)
        if (!present[i])
            for (var j = 0; j < d; j++) { cov[i, j] = 0; cov[j, i] = 0; }
        return (cov, diag);
    }
}

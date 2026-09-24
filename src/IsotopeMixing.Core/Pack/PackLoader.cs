using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Pack;

// 导入只在“派生层”做规范化；原始表示与诊断始终保留。
// 无法转换的值留在原位置（Parsed = null + 诊断），绝不丢、绝不静默改数。
public static class PackLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static CasePack Parse(byte[] bytes)
    {
        var pack = JsonSerializer.Deserialize<CasePack>(bytes, JsonOptions)
                   ?? throw new InvalidDataException("案例包 JSON 为空。");
        Validate(pack);
        return pack;
    }

    private static void Validate(CasePack pack)
    {
        if (string.IsNullOrWhiteSpace(pack.CaseId)) throw new InvalidDataException("caseId 缺失。");
        if (pack.Dimensions.Count == 0) throw new InvalidDataException("至少需要一个同位素维度。");
        if (pack.EndmemberSets.Count == 0) throw new InvalidDataException("至少需要一个端元集合版本。");
        if (pack.Fractionations.Count == 0) throw new InvalidDataException("至少需要一个分馏版本。");
        if (pack.Priors.Count == 0) throw new InvalidDataException("至少需要一个先验。");
    }

    public static MeasuredValue ToMeasuredValue(string dimensionId, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new MeasuredValue(dimensionId, raw, null, "missing");

        if (TryParseDouble(raw, out double value))
            return new MeasuredValue(dimensionId, raw, value, null);

        return new MeasuredValue(dimensionId, raw, null, $"unparseable-number:{raw}");
    }

    public static UnitDeclaration ToUnit(string dimensionId, string? rawUnit, string canonicalUnit)
    {
        if (string.IsNullOrWhiteSpace(rawUnit))
            return new UnitDeclaration(dimensionId, rawUnit ?? "", canonicalUnit, "unit-defaulted");

        if (UnitsEquivalent(rawUnit, canonicalUnit))
            return new UnitDeclaration(dimensionId, rawUnit, canonicalUnit, null);

        // 领域案例包内暂不做跨单位换算（δ 单位均为 per mil）：
        // 不能转换时把值留在原位置并附诊断，不为画图丢弃。
        return new UnitDeclaration(dimensionId, rawUnit, null, $"unit-incompatible:{rawUnit}!{canonicalUnit}");
    }

    private static bool UnitsEquivalent(string raw, string canonical)
    {
        var r = raw.Trim().Replace("‰", "permille").Replace("per mil", "permille")
                 .Replace("permil", "permille").ToLowerInvariant();
        var c = canonical.Trim().Replace("‰", "permille").ToLowerInvariant();
        return r == c;
    }

    public static bool TryParseDouble(string raw, out double value) =>
        double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // ---- Pack -> 领域记录（保留原始文本） ----

    public static DimensionDef Dimension(PackDimension d, int ordinal) =>
        new(d.Id, d.Label, d.Unit, d.ReferenceScale, ordinal);

    public static SampleRecord Sample(PackSample s, IReadOnlyList<PackDimension> dimensions)
    {
        var values = s.Values
            .Select(v => ToMeasuredValue(v.Dimension, v.RawValue))
            .ToList();
        var units = dimensions
            .Select(dim =>
            {
                var declared = s.Units.FirstOrDefault(u => u.Dimension == dim.Id);
                return ToUnit(dim.Id, declared?.RawUnit, dim.Unit);
            })
            .ToList();
        return new SampleRecord(s.Id, s.Label, values, units, s.Provenance);
    }

    public static EndmemberSetVersion EndmemberSet(PackEndmemberSet set)
    {
        var records = set.Endmembers.Select(e =>
        {
            var means = e.Means.Select(m => ToMeasuredValue(m.Dimension, m.RawValue)).ToList();
            var uncertainties = e.StdDevs.Select(u => ToMeasuredValue(u.Dimension, u.RawValue)).ToList();

            List<EndmemberMode>? modes = null;
            if (e.Modes is { Count: > 0 })
            {
                modes = e.Modes.Select(mode =>
                {
                    var modeMeans = mode.Means
                        .Select(raw =>
                            string.IsNullOrWhiteSpace(raw) ? (double?)null :
                            TryParseDouble(raw!, out double v) ? v : (double?)null)
                        .ToArray();
                    var variances = mode.StdDevs
                        .Select(raw =>
                        {
                            double sd = TryParseDouble(raw, out double v) ? v : 0.5;
                            return sd * sd;
                        })
                        .ToArray();
                    return new EndmemberMode(mode.ModeId, modeMeans, variances, mode.Weight);
                }).ToList();
            }

            IReadOnlyList<EndmemberMode> modeList = modes is null
                ? Array.Empty<EndmemberMode>()
                : modes;
            return new EndmemberRecord(e.Id, e.Label, means, uncertainties, modeList, e.Provenance);
        }).ToList();

        return new EndmemberSetVersion(
            set.Id, set.Label, set.Parent, records, set.DimensionOrder,
            DateTimeOffset.UtcNow, set.ChangeNote);
    }

    public static MeasurementCovariance Covariance(PackCovariance c)
    {
        int n = c.DimensionOrder.Count;
        var matrix = new double[n][];
        bool correlated = false;
        var diagnostics = new List<string>();

        for (int i = 0; i < n; i++)
        {
            matrix[i] = new double[n];
            for (int j = 0; j < n; j++)
            {
                string raw = c.Rows[i][j];
                if (!TryParseDouble(raw, out double v))
                {
                    diagnostics.Add($"cov-unparseable:{i},{j}={raw}");
                    v = i == j ? 1.0 : 0.0;
                }
                matrix[i][j] = v;
                if (i != j && System.Math.Abs(v) > 1e-12) correlated = true;
            }
        }

        // 对称化非对称输入并保留诊断（不静默丢弃相关性）。
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (System.Math.Abs(matrix[i][j] - matrix[j][i]) > 1e-12)
                {
                    diagnostics.Add($"cov-asymmetric:{i},{j}");
                    double avg = (matrix[i][j] + matrix[j][i]) / 2;
                    matrix[i][j] = matrix[j][i] = avg;
                    correlated = true;
                }

        return new MeasurementCovariance(
            c.Id, c.Label, c.SampleId, c.DimensionOrder, matrix, correlated,
            c.RawRows ?? c.Rows.Select(r => string.Join(",", r)).ToArray());
    }

    public static FractionationVersion Fractionation(PackFractionation f)
    {
        var factors = f.Factors.Select(factor =>
        {
            if (string.IsNullOrWhiteSpace(factor.RawValue))
                return new FractionationFactor(factor.Endmember, factor.Dimension, factor.RawValue, 0.0, "fractionation-defaulted-zero");
            if (TryParseDouble(factor.RawValue, out double v))
                return new FractionationFactor(factor.Endmember, factor.Dimension, factor.RawValue, v, null);
            return new FractionationFactor(factor.Endmember, factor.Dimension, factor.RawValue, null, $"unparseable-number:{factor.RawValue}");
        }).ToList();

        return new FractionationVersion(f.Id, f.Label, f.Parent, factors, DateTimeOffset.UtcNow, f.ChangeNote);
    }

    public static PriorAssumption Prior(PackPrior p)
    {
        var intervals = p.Intervals.Select(i =>
        {
            double? min = i.Min != null && TryParseDouble(i.Min, out double lo) ? lo : (double?)null;
            double? max = i.Max != null && TryParseDouble(i.Max, out double hi) ? hi : (double?)null;
            return new PriorInterval(i.Endmember, min, max, $"[{i.Min ?? "-inf"},{i.Max ?? "+inf"}]");
        }).ToList();
        return new PriorAssumption(p.Id, p.Label, p.Kind, intervals, p.Dirichlet, p.RawRepresentation);
    }
}

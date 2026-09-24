using IsotopeMixing.Core;
using IsotopeMixing.Ingest;

namespace IsotopeMixing.Analysis;

/// <summary>
/// 从原始层材料构建派生模型。原始 RawValue.raw_text / unit / diagnostic 永久保留；
/// 这里只做显式可转换单位的规范化，无法转换/缺失的值以 null + 诊断出现在对应位置。
/// </summary>
public sealed class Materializer
{
    private static IEnumerable<RawValue> Field(IEnumerable<RawValue> vs, string recordKey, string field) =>
        vs.Where(v => v.RecordKey == recordKey && v.Field == field);

    private static double?[] DimensionValues(IEnumerable<RawValue> vs, string recordKey,
        int dimensions, string prefix, out string[] units, out bool[] present)
    {
        var means = new double?[dimensions];
        units = new string[dimensions];
        present = new bool[dimensions];
        for (int d = 0; d < dimensions; d++)
        {
            string field = $"{prefix}{d + 1}";
            var v = Field(vs, recordKey, field).FirstOrDefault();
            if (v is not null)
            {
                means[d] = v.Value;
                units[d] = v.UnitCanonical ?? v.UnitRaw ?? "";
                present[d] = v.Usable;
            }
        }
        return means;
    }

    public SampleModel ToSample(ParsedMaterial m, int dimensions)
    {
        string key = m.Values.Select(v => v.RecordKey).First();
        var means = DimensionValues(m.Values, key, dimensions, "delta", out var units, out var present);
        var label = Field(m.Values, key, "label").FirstOrDefault()?.RawText ?? m.Label;
        var coordRaw = Field(m.Values, key, "coord").FirstOrDefault()?.RawText ?? "";
        var coord = CoordPoint.Parse(coordRaw);
        string sampleId = Field(m.Values, key, "sample_id").FirstOrDefault()?.RawText ?? key;
        return new SampleModel(m.MaterialId, sampleId, label, means, present, units,
            coord.Lon, coord.Lat, coord.RawText, coord.Diagnostic);
    }

    public EndmemberSetModel ToEndmembers(ParsedMaterial m, int dimensions)
    {
        var sources = new List<EndmemberSourceModel>();
        foreach (string key in m.Values.Select(v => v.RecordKey).Distinct().OrderBy(k => k))
        {
            var means = DimensionValues(m.Values, key, dimensions, "delta", out var units, out var present);
            string label = Field(m.Values, key, "label").FirstOrDefault()?.RawText ?? key;
            sources.Add(new EndmemberSourceModel(key, label, means, present, units));
        }
        string group = m.Values.Select(v => v.RecordKey).First();
        return new EndmemberSetModel(m.MaterialId, m.Label, m.Stage, sources);
    }

    public CovarianceModel ToCovariance(ParsedMaterial m, int dimensions)
    {
        string sampleId = m.Values.Select(v => v.RecordKey).First();
        var mat = new double[dimensions][];
        for (int i = 0; i < dimensions; i++)
        {
            mat[i] = new double[dimensions];
            for (int j = 0; j < dimensions; j++)
            {
                var v = Field(m.Values, sampleId, $"c{i + 1}_{j + 1}").FirstOrDefault();
                mat[i][j] = v?.Value ?? (i == j ? 1.0 : 0.0);
            }
        }
        bool corr = Enumerable.Range(0, dimensions).Any(i =>
            Enumerable.Range(0, i).Any(j => Math.Abs(mat[i][j]) > 1e-15));
        return new CovarianceModel(m.MaterialId, sampleId, mat, corr);
    }

    public FractionationModel ToFractionation(ParsedMaterial m, int dimensions)
    {
        string ver = m.Values.Select(v => v.RecordKey).First();
        var delta = new double[dimensions];
        var units = new string[dimensions];
        for (int d = 0; d < dimensions; d++)
        {
            var v = Field(m.Values, ver, $"delta{d + 1}").FirstOrDefault();
            delta[d] = v?.Value ?? 0;
            units[d] = v?.UnitCanonical ?? "permil";
        }
        return new FractionationModel(m.MaterialId, m.Label, delta, units);
    }

    /// <summary>每个端元一行：source_id, lower, upper（比例先验界）。</summary>
    public PriorBoundsModel ToPriors(ParsedMaterial m, int sourceCount, IReadOnlyList<string> sourceOrder)
    {
        var lo = new double?[sourceCount];
        var hi = new double?[sourceCount];
        foreach (var g in m.Values.GroupBy(v => v.RecordKey))
        {
            string sourceId = Field(m.Values, g.Key, "source_id").FirstOrDefault()?.RawText ?? g.Key;
            int idx = sourceOrder.ToList().IndexOf(sourceId);
            if (idx < 0) continue;
            var lv = Field(m.Values, g.Key, "lower").FirstOrDefault();
            var uv = Field(m.Values, g.Key, "upper").FirstOrDefault();
            lo[idx] = lv?.Usable == true ? lv.Value : null;
            hi[idx] = uv?.Usable == true ? uv.Value : null;
        }
        return new PriorBoundsModel(m.MaterialId, m.Label, lo, hi);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using IsotopeMix.Core.Models;

namespace IsotopeMix.Core.Solver;

// 规范化后的同位素空间（派生层）。缺失维以 null 表示；无法转换的值保留 null 并附诊断。
public sealed class NormalizedMaterial
{
    public string Id { get; init; } = "";
    public MaterialRole Role { get; init; }
    public string Name { get; init; } = "";
    // 与 NormalizedDataset.Tracers 对齐；null = 该维缺失或无法转换。
    public double?[] Coordinates { get; set; } = Array.Empty<double?>();
    public bool[] ValueWasRaw { get; set; } = Array.Empty<bool>();
}

public sealed class NormalizedDataset
{
    public List<string> Tracers { get; init; } = new();
    public List<string> CanonicalUnits { get; init; } = new();
    public List<string> ReferenceFrames { get; init; } = new();
    public List<NormalizedMaterial> Materials { get; init; } = new();
    public List<ConversionDiagnostic> Diagnostics { get; init; } = new();
    // 每个 tracer 可参与求解的材料行（样品与端元在该维都有效）。
}

// 单位换算器：只在派生层规范化；原始记录不被修改。
public sealed class Normalizer
{
    private readonly Dictionary<string, UnitDefinition> _units;

    public Normalizer(IEnumerable<UnitDefinition> units)
    {
        _units = units.ToDictionary(u => u.Unit, u => u, StringComparer.OrdinalIgnoreCase);
    }

    public NormalizedDataset Normalize(IEnumerable<MaterialRecord> materials,
                                       IEnumerable<CovarianceRecord> covariances)
    {
        var mats = materials.ToList();
        var diagnostics = new List<ConversionDiagnostic>();

        // 维度并集：保留所有出现过的 tracer，缺失同位素维在坐标中为 null。
        var tracers = new List<string>();
        foreach (var m in mats)
            foreach (var v in m.Values)
                if (!tracers.Contains(v.Tracer)) tracers.Add(v.Tracer);

        var units = new List<string>();
        var frames = new List<string>();
        foreach (var t in tracers)
        {
            string unit = "", frame = "";
            foreach (var m in mats)
            {
                var v = m.Values.FirstOrDefault(x => x.Tracer == t);
                if (v is null) continue;
                if (unit == "") unit = CanonicalUnitOf(v, diagnostics, m);
                if (frame == "") frame = v.ReferenceFrame;
            }
            units.Add(unit);
            frames.Add(frame);
        }

        var normalized = new List<NormalizedMaterial>();
        foreach (var m in mats)
        {
            var coords = new double?[tracers.Count];
            var wasRaw = new bool[tracers.Count];
            for (int i = 0; i < tracers.Count; i++)
            {
                var v = m.Values.FirstOrDefault(x => x.Tracer == tracers[i]);
                if (v is null)
                {
                    coords[i] = null; // 缺失同位素维：明确缺失，不填零伪造
                    continue;
                }
                if (!v.Value.HasValue)
                {
                    coords[i] = null;
                    diagnostics.Add(new ConversionDiagnostic
                    {
                        RecordId = m.Id, Tracer = v.Tracer, RawText = v.RawText,
                        Unit = v.Unit, Problem = "value-unparseable"
                    });
                    continue;
                }
                if (!_units.TryGetValue(v.Unit, out var def))
                {
                    coords[i] = null; // 无法转换：留在原位置并附诊断，绝不为画图丢弃
                    diagnostics.Add(new ConversionDiagnostic
                    {
                        RecordId = m.Id, Tracer = v.Tracer, RawText = v.RawText,
                        Unit = v.Unit, Problem = "unknown-unit"
                    });
                    wasRaw[i] = true;
                    continue;
                }
                coords[i] = def.ToCanonicalScale * v.Value.Value + def.ToCanonicalOffset;
            }
            normalized.Add(new NormalizedMaterial
            {
                Id = m.Id, Role = m.Role, Name = m.Name,
                Coordinates = coords, ValueWasRaw = wasRaw
            });
        }

        return new NormalizedDataset
        {
            Tracers = tracers, CanonicalUnits = units, ReferenceFrames = frames,
            Materials = normalized, Diagnostics = diagnostics
        };
    }

    private string CanonicalUnitOf(IsotopeValue v, List<ConversionDiagnostic> diagnostics, MaterialRecord m)
    {
        if (_units.TryGetValue(v.Unit, out var def)) return def.CanonicalUnit;
        if (!diagnostics.Any(d => d.RecordId == m.Id && d.Tracer == v.Tracer))
            diagnostics.Add(new ConversionDiagnostic
            {
                RecordId = m.Id, Tracer = v.Tracer, RawText = v.RawText,
                Unit = v.Unit, Problem = "unknown-unit"
            });
        return v.Unit;
    }

    // 把协方差矩阵换算到规范单位（对角尺度变换：C' = D C D，D=scale）。
    public double[,]? NormalizeCovariance(CovarianceRecord cov, NormalizedDataset ds,
                                          out List<string> problems)
    {
        problems = new List<string>();
        int k = cov.Tracers.Count;
        var result = new double[k, k];
        var scales = new double[k];
        for (int i = 0; i < k; i++)
        {
            if (!_units.TryGetValue(cov.Units[i], out var def))
            {
                problems.Add($"unknown-unit:{cov.Units[i]}");
                return null;
            }
            scales[i] = def.ToCanonicalScale;
        }
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++)
                result[i, j] = cov.Matrix[i, j] * scales[i] * scales[j];
        return result;
    }
}

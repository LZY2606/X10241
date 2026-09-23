using System.Text.Json;
using IsotopeMix.Core.Models;
using IsotopeMix.Core.Units;

namespace IsotopeMix.Core.Interpret;

/// <summary>解析诊断：无法规范化的数值留在原位，绝不丢弃。</summary>
public sealed record InterpretDiagnostic(string RecordId, string Field, string Message, string RawText);

/// <summary>
/// 把来源层的原始 JSON 解释为规范化领域对象。
/// 解释是派生层：相同 RawSha256 永远得到相同结果；不允许不可逆清洗。
/// </summary>
public sealed class Interpreter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public (EndmemberDistribution? Endmember, SampleRecord? Sample, CovarianceRecord? Cov,
        FractionationRecord? Frac, PriorRecord? Prior, UnitDeclaration? Unit,
        List<InterpretDiagnostic> Diagnostics) Interpret(RawRecord raw)
    {
        var diags = new List<InterpretDiagnostic>();
        using var doc = JsonDocument.Parse(raw.RawPayload);
        var root = doc.RootElement;

        EndmemberDistribution? em = null;
        SampleRecord? sample = null;
        CovarianceRecord? cov = null;
        FractionationRecord? frac = null;
        PriorRecord? prior = null;
        UnitDeclaration? unit = null;

        switch (raw.Kind)
        {
            case RecordKind.Sample:
                sample = ReadSample(raw, root, diags);
                break;
            case RecordKind.Endmember:
                em = ReadEndmember(raw, root, diags);
                break;
            case RecordKind.Covariance:
                cov = ReadCovariance(raw, root);
                break;
            case RecordKind.Fractionation:
                frac = ReadFractionation(raw, root);
                break;
            case RecordKind.Prior:
                prior = ReadPrior(raw, root);
                break;
            case RecordKind.UnitDeclaration:
                unit = ReadUnit(raw, root);
                break;
        }

        return (em, sample, cov, frac, prior, unit, diags);
    }

    private static UnitDeclaration ReadUnit(RawRecord raw, JsonElement root)
    {
        string S(string k, string d) => root.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? d : d;
        double? N(string k) => root.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetDouble() : null;
        return new UnitDeclaration
        {
            Id = raw.StableClientId,
            Dimension = S("dimension", ""),
            KindText = S("kind", "permil"),
            Reference = S("reference", ""),
            Scale = N("scale")
        };
    }

    private static UnitDef UnitFor(string dimension, Dictionary<string, UnitDeclaration> units)
    {
        if (units.TryGetValue(dimension, out var u))
        {
            var kind = u.KindText.ToLowerInvariant() switch
            {
                "permil" or "permille" or "‰" => UnitKind.Permil,
                "percent" or "%" => UnitKind.Percent,
                "ratio" or "r" => UnitKind.Ratio,
                "concentration" => UnitKind.Concentration,
                _ => UnitKind.Dimensionless
            };
            return new UnitDef(kind, u.Reference, u.KindText, u.Scale);
        }
        return UnitDef.PermilVpdb;
    }

    private static MeasuredValue ReadMeasured(string dim, JsonElement el,
        Dictionary<string, UnitDeclaration> units, string recordId,
        List<InterpretDiagnostic> diags)
    {
        double rawValue;
        double? rawSigma = null;
        string rawText = el.GetRawText();

        if (el.ValueKind == JsonValueKind.Number)
        {
            rawValue = el.GetDouble();
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            if (!el.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number)
            {
                var mv = new MeasuredValue { RawText = rawText, Diagnostic = "缺少数值字段，原始表示保留" };
                diags.Add(new InterpretDiagnostic(recordId, dim, mv.Diagnostic, rawText));
                return mv;
            }
            rawValue = v.GetDouble();
            if (el.TryGetProperty("sigma", out var s) && s.ValueKind == JsonValueKind.Number)
                rawSigma = s.GetDouble();
        }
        else
        {
            var mv = new MeasuredValue { RawText = rawText, Diagnostic = $"无法解析的同位素值，原始表示保留：{rawText}" };
            diags.Add(new InterpretDiagnostic(recordId, dim, mv.Diagnostic, rawText));
            return mv;
        }

        var unit = UnitFor(dim, units);
        var canonical = unit.ToPermil(rawValue, out var diag);
        double? canonicalSigma = null;
        if (rawSigma is { } rs)
        {
            var cs = unit.ToPermil(rs, out var sdiag);
            canonicalSigma = cs;
            if (sdiag != null) diags.Add(new InterpretDiagnostic(recordId, dim + ".sigma", sdiag, rawText));
        }
        if (diag != null) diags.Add(new InterpretDiagnostic(recordId, dim, diag, rawText));

        return new MeasuredValue
        {
            RawValue = rawValue,
            RawSigma = rawSigma,
            RawText = rawText,
            CanonicalPermil = canonical,
            CanonicalSigmaPermil = canonicalSigma
        };
    }

    private SampleRecord ReadSample(RawRecord raw, JsonElement root, List<InterpretDiagnostic> diags)
    {
        var units = ReadInlineUnits(root);
        var rec = new SampleRecord
        {
            Id = raw.StableClientId,
            MaterialKey = String(root, "materialKey", raw.MaterialKey),
            Name = String(root, "name", raw.StableClientId)
        };
        if (root.TryGetProperty("isotopes", out var iso) && iso.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in iso.EnumerateObject())
                rec.Isotopes[p.Name] = ReadMeasured(p.Name, p.Value, units, rec.Id, diags);
        }
        if (root.TryGetProperty("covarianceRef", out var cr) && cr.ValueKind == JsonValueKind.String)
            rec.CovarianceRef = cr.GetString();
        if (root.TryGetProperty("priorRef", out var pr) && pr.ValueKind == JsonValueKind.String)
            rec.PriorRef = pr.GetString();
        return rec;
    }

    private EndmemberDistribution ReadEndmember(RawRecord raw, JsonElement root, List<InterpretDiagnostic> diags)
    {
        var units = ReadInlineUnits(root);
        var em = new EndmemberDistribution
        {
            Id = raw.StableClientId,
            MaterialKey = String(root, "materialKey", raw.MaterialKey),
            Name = String(root, "name", raw.StableClientId),
            VersionLabel = String(root, "version", "v1"),
            Distributional = Bool(root, "distributional", false)
        };
        if (root.TryGetProperty("members", out var ms) && ms.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in ms.EnumerateArray())
            {
                var member = new EndmemberMember { MemberName = String(m, "name", "?") };
                if (m.TryGetProperty("isotopes", out var iso) && iso.ValueKind == JsonValueKind.Object)
                    foreach (var p in iso.EnumerateObject())
                        member.Isotopes[p.Name] = ReadMeasured(p.Name, p.Value, units, em.Id, diags);
                em.Members.Add(member);
            }
        }
        return em;
    }

    private static CovarianceRecord ReadCovariance(RawRecord raw, JsonElement root)
    {
        var dims = new List<string>();
        if (root.TryGetProperty("dimensions", out var d) && d.ValueKind == JsonValueKind.Array)
            foreach (var x in d.EnumerateArray()) dims.Add(x.GetString() ?? "");
        int n = dims.Count;
        var mat = new double[n, n];
        if (root.TryGetProperty("matrix", out var mx) && mx.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var row in mx.EnumerateArray())
            {
                int j = 0;
                foreach (var cell in row.EnumerateArray())
                {
                    if (i < n && j < n && cell.ValueKind == JsonValueKind.Number)
                        mat[i, j] = cell.GetDouble();
                    j++;
                }
                i++;
            }
        }
        return new CovarianceRecord { Id = raw.StableClientId, Name = String(root, "name", raw.StableClientId), Dimensions = dims, Matrix = mat };
    }

    private static FractionationRecord ReadFractionation(RawRecord raw, JsonElement root)
    {
        var f = new FractionationRecord
        {
            Id = raw.StableClientId,
            Name = String(root, "name", raw.StableClientId),
            VersionLabel = String(root, "version", "v1")
        };
        if (root.TryGetProperty("offsets", out var o) && o.ValueKind == JsonValueKind.Object)
            foreach (var p in o.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number) f.OffsetsPermil[p.Name] = p.Value.GetDouble();
        return f;
    }

    private static PriorRecord ReadPrior(RawRecord raw, JsonElement root)
    {
        var p = new PriorRecord { Id = raw.StableClientId, Name = String(root, "name", raw.StableClientId) };
        ReadBounds(root, "lower", p.LowerFraction);
        ReadBounds(root, "upper", p.UpperFraction);
        return p;
    }

    private static void ReadBounds(JsonElement root, string key, Dictionary<string, double> target)
    {
        if (root.TryGetProperty(key, out var b) && b.ValueKind == JsonValueKind.Object)
            foreach (var q in b.EnumerateObject())
                if (q.Value.ValueKind == JsonValueKind.Number) target[q.Name] = q.Value.GetDouble();
    }

    private static Dictionary<string, UnitDeclaration> ReadInlineUnits(JsonElement root)
    {
        var map = new Dictionary<string, UnitDeclaration>();
        if (root.TryGetProperty("units", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in u.EnumerateObject())
            {
                var decl = new UnitDeclaration { Dimension = p.Name };
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    decl.KindText = p.Value.GetString() ?? "permil";
                }
                else if (p.Value.ValueKind == JsonValueKind.Object)
                {
                    decl.KindText = String(p.Value, "kind", "permil");
                    decl.Reference = String(p.Value, "reference", "");
                    if (p.Value.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Number)
                        decl.Scale = sc.GetDouble();
                }
                map[p.Name] = decl;
            }
        }
        return map;
    }

    private static string String(JsonElement e, string key, string dflt)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? dflt : dflt;

    private static bool Bool(JsonElement e, string key, bool dflt)
        => e.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : dflt;
}

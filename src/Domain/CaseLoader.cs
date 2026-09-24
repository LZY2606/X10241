using System.Text.Json;

namespace IsotopeMix.Domain;

public sealed class CaseStep
{
    public string Step { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Sample { get; set; } = "";
    public string EndmemberSet { get; set; } = "";
    public string Covariance { get; set; } = "";
    public string Fractionation { get; set; } = "";
    public string Prior { get; set; } = "";
    public string ExpectVerdict { get; set; } = "";
}

public sealed class CasePack
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Narrative { get; set; } = "";
    public JsonElement BatchJson { get; set; }
    public List<CaseStep> Replay { get; set; } = new();
}

public static class CaseLoader
{
    public static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CasePack Load(string directory)
    {
        var json = File.ReadAllText(Path.Combine(directory, "case.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var pack = new CasePack
        {
            Id = root.GetProperty("id").GetString()!,
            Title = root.GetProperty("title").GetString()!,
            Domain = root.GetProperty("domain").GetString()!,
            Narrative = root.GetProperty("narrative").GetString()!,
            BatchJson = root.GetProperty("batch").Clone(),
            Replay = JsonSerializer.Deserialize<List<CaseStep>>(
                root.GetProperty("replay").GetRawText(), ReadOpts)!
        };
        return pack;
    }

    public static (RawBatch batch, byte[] rawPayload, List<Diagnostic> extra) BuildBatch(CasePack pack)
    {
        var b = pack.BatchJson;
        var batchId = b.GetProperty("batchId").GetString()!;
        var batch = new RawBatch
        {
            BatchId = batchId,
            Label = b.GetProperty("label").GetString()!,
            Origin = b.GetProperty("origin").GetString()!,
            ImportedAt = "",
            RawPayloadSha256 = ""
        };

        foreach (var s in b.GetProperty("samples").EnumerateArray())
        {
            var sample = new RawSample
            {
                Id = s.GetProperty("id").GetString()!,
                Code = s.GetProperty("code").GetString()!,
                Material = s.GetProperty("material").GetString()!,
                Site = s.GetProperty("site").GetString()!,
                Notes = s.TryGetProperty("notes", out var nt)
                    ? nt.EnumerateArray().Select(x => x.GetString()!).ToList() : new()
            };
            foreach (var prop in s.GetProperty("deltas").EnumerateObject())
                sample.Deltas[prop.Name] = QuantityParser.ParseDelta(prop.Name, prop.Value.GetString());
            batch.Samples.Add(sample);
        }

        foreach (var e in b.GetProperty("endmemberSets").EnumerateArray())
        {
            var set = new RawEndmemberSet
            {
                Id = e.GetProperty("id").GetString()!,
                Label = e.GetProperty("label").GetString()!
            };
            foreach (var m in e.GetProperty("members").EnumerateArray())
            {
                var member = new RawEndmember
                {
                    Code = m.GetProperty("code").GetString()!,
                    Label = m.GetProperty("label").GetString()!,
                    ModeId = m.GetProperty("modeId").GetString()!,
                    ModeLabel = m.TryGetProperty("modeLabel", out var ml) ? ml.GetString() : null
                };
                foreach (var prop in m.GetProperty("means").EnumerateObject())
                    member.Means[prop.Name] = QuantityParser.ParseDelta(prop.Name, prop.Value.GetString());
                if (m.TryGetProperty("sd", out var sd))
                    foreach (var prop in sd.EnumerateObject())
                        member.Sd[prop.Name] = prop.Value.GetDouble();
                set.Members.Add(member);
            }
            batch.EndmemberSets.Add(set);
        }

        foreach (var c in b.GetProperty("covariances").EnumerateArray())
        {
            var dims = c.GetProperty("dimensions").EnumerateArray().Select(x => x.GetString()!).ToList();
            var rows = c.GetProperty("matrix").EnumerateArray().Select(r =>
                r.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToArray();
            var matrix = new double[rows.Length][];
            var correlated = false;
            for (var i = 0; i < rows.Length; i++)
            {
                matrix[i] = new double[rows.Length];
                for (var j = 0; j < rows.Length; j++)
                {
                    matrix[i][j] = rows[i][j];
                    if (i != j && Math.Abs(rows[i][j]) > 1e-12) correlated = true;
                }
            }
            batch.Covariances.Add(new RawCovariance
            {
                Id = c.GetProperty("id").GetString()!,
                SampleId = c.GetProperty("sampleId").GetString()!,
                Dimensions = dims,
                Matrix = matrix,
                Record = c.GetProperty("record").GetString()!,
                HasCorrelation = correlated
            });
        }

        foreach (var f in b.GetProperty("fractionations").EnumerateArray())
        {
            var shifts = new Dictionary<string, double>();
            foreach (var prop in f.GetProperty("shift").EnumerateObject())
                shifts[prop.Name] = prop.Value.GetDouble();
            batch.Fractionations.Add(new RawFractionation
            {
                Id = f.GetProperty("id").GetString()!,
                Label = f.GetProperty("label").GetString()!,
                Basis = f.GetProperty("basis").GetString()!,
                Shift = shifts
            });
        }

        foreach (var p in b.GetProperty("priors").EnumerateArray())
        {
            var bounds = new Dictionary<string, double[]>();
            foreach (var prop in p.GetProperty("bounds").EnumerateObject())
                bounds[prop.Name] = prop.Value.EnumerateArray().Select(x => x.GetDouble()).ToArray();
            batch.Priors.Add(new RawPrior
            {
                Id = p.GetProperty("id").GetString()!,
                Label = p.GetProperty("label").GetString()!,
                Basis = p.GetProperty("basis").GetString()!,
                Bounds = bounds
            });
        }

        var extras = new List<Diagnostic>();
        if (b.TryGetProperty("unconvertible", out var un))
            foreach (var u in un.EnumerateArray())
                extras.Add(new Diagnostic
                {
                    Severity = "info",
                    Code = "raw-retention-note",
                    Where = u.GetProperty("where").GetString()!,
                    Message = $"原始保留：{u.GetProperty("raw").GetString()}；{u.GetProperty("diagnostic").GetString()}"
                });
        batch.Diagnostics.AddRange(extras);

        // 汇总规范化诊断，全部作为可追溯记录保留（不阻断导入）。
        foreach (var s in batch.Samples)
        foreach (var q in s.Deltas.Values)
            if (q.Status != "ok")
                batch.Diagnostics.Add(new Diagnostic
                {
                    Severity = q.Status == "missing" ? "info" : "warning",
                    Code = $"quantity-{q.Status}",
                    Where = $"{s.Id}.{q.Dimension}",
                    Message = q.Diagnostic ?? ""
                });

        var rawPayload = System.Text.Encoding.UTF8.GetBytes(pack.BatchJson.GetRawText());
        return (batch, rawPayload, extras);
    }
}

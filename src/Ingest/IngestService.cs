using System.Text;
using System.Text.Json;
using IsotopeMixing.Core;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Ingest;

public sealed record IngestFile(string Filename, string MaterialType, string Stage, byte[] Content);

public sealed class IngestException : Exception
{
    public IngestException(string message) : base(message) { }
}

/// <summary>
/// 导入器：原始字节落 raw_documents，每个单元格落 raw_values（保留原始文本/单位/诊断）。
/// 结构错误（缺列、空文件等）直接抛出，由上层在同一事务回滚，不留半份记录。
/// 单格不可转换不阻断导入：值留在原位置并附诊断。
/// </summary>
public sealed class IngestService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public Store.ImportUnit BuildUnit(string caseId, IngestFile file, string importRef)
    {
        string type = file.MaterialType.ToLowerInvariant();
        var rows = Csv.Parse(file.Content);
        if (rows.Count == 0) throw new IngestException($"{file.Filename}: 空文件或无数据行");

        var values = new List<RawValue>();
        var diagnostics = new List<string>();
        string label = Path.GetFileNameWithoutExtension(file.Filename);
        string kind = type switch
        {
            "samples" => MaterialKind.Sample.ToString(),
            "endmembers" => MaterialKind.EndmemberSet.ToString(),
            "covariance" => MaterialKind.Covariance.ToString(),
            "fractionation" => MaterialKind.Fractionation.ToString(),
            "priors" => MaterialKind.Priors.ToString(),
            _ => throw new IngestException($"{file.Filename}: 未知材料类型 {file.MaterialType}")
        };

        string sha = Deterministic.Sha256(file.Content);
        string versionOf = "";
        string materialId = Deterministic.MaterialId(caseId, kind, file.Stage, versionOf, sha);

        switch (type)
        {
            case "samples": BuildSamples(rows, materialId, values, diagnostics); break;
            case "endmembers": BuildEndmembers(rows, materialId, values, diagnostics, ref label); break;
            case "covariance": BuildCovariance(rows, materialId, values, diagnostics); break;
            case "fractionation": BuildFractionation(rows, materialId, values, diagnostics, ref label); break;
            case "priors": BuildPriors(rows, materialId, values, diagnostics, ref label); break;
        }

        var doc = new RawDocument(
            Deterministic.DocumentId(materialId, file.Filename, sha),
            materialId, kind, file.Stage, "text/csv", file.Filename,
            file.Content, sha, DateTimeOffset.UtcNow, importRef);
        var mat = new ParsedMaterial(materialId, Enum.Parse<MaterialKind>(kind),
            label, file.Stage, versionOf, DateTimeOffset.UtcNow, values, diagnostics);

        var payload = JsonSerializer.Serialize(new
        {
            materialId, kind, stage = file.Stage, filename = file.Filename,
            sha256 = sha, rows = rows.Count,
            hardErrors = diagnostics.Where(d => IsHard(d)).ToList()
        }, JsonOpts);

        return new Store.ImportUnit(doc, mat, values, EventType.MaterialImported, materialId,
            JsonDocument.Parse(payload).RootElement.Clone());
    }

    private static bool IsHard(string d) =>
        d.StartsWith("structural:", StringComparison.Ordinal) ||
        d.StartsWith("unparseable-", StringComparison.Ordinal);

    private static void Add(List<RawValue> values, List<string> diagnostics,
        string materialId, string key, string field, string raw, string? unit)
    {
        var (v, canon, err) = Units.Convert(raw, unit);
        string id = Deterministic.ValueId(materialId, key, field);
        values.Add(new RawValue(id, materialId, key, field, raw, unit,
            err is null ? v : err is "missing" ? null : v,
            canon, err));
        if (err is not null and not "missing")
            diagnostics.Add($"{key}.{field}: {err} (raw=\"{raw}\")");
    }

    private static void RequireColumns(List<Dictionary<string, string>> rows, string file,
        params string[] required)
    {
        var keys = rows[0].Keys.ToHashSet();
        var missing = required.Where(r => !keys.Contains(r)).ToList();
        if (missing.Count > 0)
            throw new IngestException($"{file}: structural: 缺少必需列 {string.Join(",", missing)}");
    }

    private static void BuildSamples(List<Dictionary<string, string>> rows, string mid,
        List<RawValue> vals, List<string> diags)
    {
        RequireColumns(rows, "samples.csv", "sample_id", "delta1");
        foreach (var row in rows)
        {
            string key = row["sample_id"];
            if (string.IsNullOrWhiteSpace(key))
                throw new IngestException("samples.csv: structural: sample_id 不能为空");
            Add(vals, diags, mid, key, "sample_id", key, null);
            Add(vals, diags, mid, key, "label", row.GetValueOrDefault("label", key), null);
            Add(vals, diags, mid, key, "coord", row.GetValueOrDefault("coord", ""), null);
            int d = 1;
            foreach (string col in row.Keys.Where(k => k.StartsWith("delta")))
            {
                Add(vals, diags, mid, key, col, row[col],
                    row.GetValueOrDefault($"unit{d}", "permil"));
                d++;
            }
        }
    }

    private static void BuildEndmembers(List<Dictionary<string, string>> rows, string mid,
        List<RawValue> vals, List<string> diags, ref string label)
    {
        RequireColumns(rows, "endmembers.csv", "source_id", "delta1");
        string? group = rows[0].GetValueOrDefault("group");
        if (!string.IsNullOrWhiteSpace(group)) label = group;
        foreach (var row in rows)
        {
            string key = row["source_id"];
            if (string.IsNullOrWhiteSpace(key))
                throw new IngestException("endmembers.csv: structural: source_id 不能为空");
            Add(vals, diags, mid, key, "source_id", key, null);
            Add(vals, diags, mid, key, "label", row.GetValueOrDefault("label", key), null);
            Add(vals, diags, mid, key, "group", row.GetValueOrDefault("group", ""), null);
            int d = 1;
            foreach (string col in row.Keys.Where(k => k.StartsWith("delta")))
            {
                Add(vals, diags, mid, key, col, row[col],
                    row.GetValueOrDefault($"unit{d}", "permil"));
                d++;
            }
        }
    }

    private static void BuildCovariance(List<Dictionary<string, string>> rows, string mid,
        List<RawValue> vals, List<string> diags)
    {
        RequireColumns(rows, "covariance.csv", "sample_id", "c1_1");
        string key = rows[0]["sample_id"];
        foreach (var row in rows)
        {
            if (row["sample_id"] != key)
                throw new IngestException("covariance.csv: structural: 一个文件只允许一个 sample_id");
            foreach (var col in row.Keys.Where(k => k.StartsWith("c") && k.Contains('_')))
                Add(vals, diags, mid, key, col, row[col], null);
        }
    }

    private static void BuildFractionation(List<Dictionary<string, string>> rows, string mid,
        List<RawValue> vals, List<string> diags, ref string label)
    {
        RequireColumns(rows, "fractionation.csv", "version_id", "delta1");
        string key = rows[0]["version_id"];
        label = key;
        foreach (var row in rows)
        {
            if (row["version_id"] != key)
                throw new IngestException("fractionation.csv: structural: 一个文件只允许一个 version_id");
            int d = 1;
            foreach (string col in row.Keys.Where(k => k.StartsWith("delta")))
            {
                Add(vals, diags, mid, key, col, row[col],
                    row.GetValueOrDefault($"unit{d}", "permil"));
                d++;
            }
        }
    }

    private static void BuildPriors(List<Dictionary<string, string>> rows, string mid,
        List<RawValue> vals, List<string> diags, ref string label)
    {
        RequireColumns(rows, "priors.csv", "version_id", "source_id");
        string ver = rows[0]["version_id"];
        label = ver;
        foreach (var row in rows)
        {
            if (row["version_id"] != ver)
                throw new IngestException("priors.csv: structural: 一个文件只允许一个 version_id");
            string key = $"{ver}:{row["source_id"]}";
            Add(vals, diags, mid, key, "source_id", row["source_id"], null);
            Add(vals, diags, mid, key, "lower", row.GetValueOrDefault("lower", ""), null);
            Add(vals, diags, mid, key, "upper", row.GetValueOrDefault("upper", ""), null);
        }
    }
}

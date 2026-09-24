using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Store.Serialization;

// 领域对象与数据库 JSON 列之间的映射。保留原始表示（raw 文本、诊断、单位）。
public static class DomainJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    // ---- 端元 ----
    public static JsonArray EndmembersToJson(IReadOnlyList<EndmemberRecord> endmembers)
    {
        var array = new JsonArray();
        foreach (var e in endmembers)
        {
            array.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["label"] = e.Label,
                ["means"] = ValuesToJson(e.Means),
                ["uncertainties"] = ValuesToJson(e.Uncertainties),
                ["modes"] = ModesToJson(e.Modes),
                ["provenance"] = e.ProvenanceNote
            });
        }
        return array;
    }

    public static List<EndmemberRecord> EndmembersFromJson(JsonArray array)
    {
        var list = new List<EndmemberRecord>();
        foreach (var node in array.Cast<JsonObject>())
        {
            list.Add(new EndmemberRecord(
                node["id"]!.GetValue<string>(),
                node["label"]!.GetValue<string>(),
                ValuesFromJson(node["means"]!.AsArray()),
                ValuesFromJson(node["uncertainties"]!.AsArray()),
                ModesFromJson(node["modes"]!.AsArray()),
                node["provenance"]?.GetValue<string>()));
        }
        return list;
    }

    private static JsonArray ValuesToJson(IReadOnlyList<MeasuredValue> values)
    {
        var array = new JsonArray();
        foreach (var v in values)
        {
            array.Add(new JsonObject
            {
                ["dimension"] = v.DimensionId,
                ["raw"] = v.Value,
                ["parsed"] = v.Parsed,
                ["diagnostic"] = v.ParseDiagnostic
            });
        }
        return array;
    }

    private static List<MeasuredValue> ValuesFromJson(JsonArray array)
    {
        var list = new List<MeasuredValue>();
        foreach (var node in array.Cast<JsonObject>())
        {
            list.Add(new MeasuredValue(
                node["dimension"]!.GetValue<string>(),
                node["raw"]?.GetValue<string>(),
                node["parsed"]?.GetValue<double>(),
                node["diagnostic"]?.GetValue<string>()));
        }
        return list;
    }

    private static JsonArray ModesToJson(IReadOnlyList<EndmemberMode> modes)
    {
        var array = new JsonArray();
        foreach (var m in modes)
        {
            var means = new JsonArray();
            foreach (var mean in m.Mean) means.Add(mean);
            var variance = new JsonArray();
            foreach (var v in m.Variance) variance.Add(v);
            array.Add(new JsonObject
            {
                ["modeId"] = m.ModeId,
                ["mean"] = means,
                ["variance"] = variance,
                ["weight"] = m.Weight
            });
        }
        return array;
    }

    private static List<EndmemberMode> ModesFromJson(JsonArray array)
    {
        var list = new List<EndmemberMode>();
        foreach (var node in array.Cast<JsonObject>())
        {
            var mean = node["mean"]!.AsArray().Select(x => x?.GetValue<double?>()).ToList<double?>();
            var variance = node["variance"]!.AsArray().Select(x => x!.GetValue<double>()).ToList();
            list.Add(new EndmemberMode(
                node["modeId"]!.GetValue<string>(),
                mean,
                variance,
                node["weight"]!.GetValue<double>()));
        }
        return list;
    }

    // ---- 样品单位 ----
    public static JsonArray UnitsToJson(IReadOnlyList<UnitDeclaration> units)
    {
        var array = new JsonArray();
        foreach (var u in units)
        {
            array.Add(new JsonObject
            {
                ["dimension"] = u.DimensionId,
                ["rawUnit"] = u.RawUnit,
                ["normalizedUnit"] = u.NormalizedUnit,
                ["diagnostic"] = u.ConversionDiagnostic
            });
        }
        return array;
    }

    public static List<UnitDeclaration> UnitsFromJson(JsonArray array)
    {
        var list = new List<UnitDeclaration>();
        foreach (var node in array.Cast<JsonObject>())
        {
            list.Add(new UnitDeclaration(
                node["dimension"]!.GetValue<string>(),
                node["rawUnit"]!.GetValue<string>(),
                node["normalizedUnit"]?.GetValue<string>(),
                node["diagnostic"]?.GetValue<string>()));
        }
        return list;
    }

    public static JsonArray StringsToJson(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var v in values) array.Add(v);
        return array;
    }

    public static List<string> StringsFromJson(JsonArray array) =>
        array.Select(x => x!.GetValue<string>()).ToList();

    public static JsonObject FactorsToJson(IReadOnlyList<FractionationFactor> factors)
    {
        var obj = new JsonObject();
        foreach (var f in factors)
        {
            obj[$"{f.EndmemberId}|{f.DimensionId}"] = new JsonObject
            {
                ["raw"] = f.RawValue,
                ["parsed"] = f.Parsed,
                ["diagnostic"] = f.ParseDiagnostic
            };
        }
        return obj;
    }

    public static List<FractionationFactor> FactorsFromJson(JsonObject obj)
    {
        var list = new List<FractionationFactor>();
        foreach (var kv in obj)
        {
            var parts = kv.Key.Split('|');
            var node = kv.Value!.AsObject();
            list.Add(new FractionationFactor(
                parts[0], parts[1],
                node["raw"]?.GetValue<string>(),
                node["parsed"]?.GetValue<double>(),
                node["diagnostic"]?.GetValue<string>()));
        }
        return list;
    }

    public static JsonObject IntervalsToJson(IReadOnlyList<PriorInterval> intervals)
    {
        var obj = new JsonObject();
        foreach (var i in intervals)
        {
            obj[i.EndmemberId] = new JsonObject
            {
                ["min"] = i.Min,
                ["max"] = i.Max,
                ["raw"] = i.RawText
            };
        }
        return obj;
    }

    public static List<PriorInterval> IntervalsFromJson(JsonObject obj)
    {
        var list = new List<PriorInterval>();
        foreach (var kv in obj)
        {
            var node = kv.Value!.AsObject();
            list.Add(new PriorInterval(
                kv.Key,
                node["min"]?.GetValue<double?>(),
                node["max"]?.GetValue<double?>(),
                node["raw"]?.GetValue<string>() ?? ""));
        }
        return list;
    }

    public static JsonObject DirichletToJson(IReadOnlyDictionary<string, double>? alphas)
    {
        var obj = new JsonObject();
        if (alphas is null) return obj;
        foreach (var kv in alphas.OrderBy(kv => kv.Key)) obj[kv.Key] = kv.Value;
        return obj;
    }
}

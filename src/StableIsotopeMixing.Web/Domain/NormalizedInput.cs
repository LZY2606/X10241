using System.Text.Json.Nodes;
using StableIsotopeMixing.Web.Data;

namespace StableIsotopeMixing.Web.Domain;

public sealed record DimInfo(string Id, string Label, string Unit, string Reference);

public sealed record EndmemberNorm(string Id, string Label, List<double[]> Modes);

public sealed record Dependency(string RawId, int Version, string Kind, string ContentHash);

/// <summary>
/// Derived-layer normalized view. Raw representations are never modified by the normalizer;
/// unconvertible or missing values are kept in RawSample and surface as diagnostics.
/// </summary>
public sealed record NormalizedInput(
    List<DimInfo> Dims,
    JsonNode?[] RawSample,
    double?[] Sample,
    List<string> Diagnostics,
    List<EndmemberNorm> Endmembers,
    double[,] SampleCovariance,
    Dictionary<string, double[,]> EndmemberCovariances,
    Dictionary<string, double[]> Offsets,
    (double Lower, double Upper)[] PriorBounds,
    string FractionationVersion,
    List<Dependency> Dependencies);

public static class DerivedNormalizer
{
    public static NormalizedInput Normalize(
        RawRow units,
        RawRow sample,
        RawRow endmembers,
        RawRow covariance,
        RawRow fractionation,
        RawRow prior)
    {
        var diagnostics = new List<string>();
        var unitsNode = JsonNode.Parse(units.RawJson)!.AsObject();
        var dims = new List<DimInfo>();
        var dimIndex = new Dictionary<string, int>();
        foreach (var item in unitsNode["dims"]!.AsArray())
        {
            var obj = item!.AsObject();
            var dim = new DimInfo(obj["id"]!.GetValue<string>(), obj["label"]!.GetValue<string>(),
                obj["unit"]!.GetValue<string>(), obj["reference"]!.GetValue<string>());
            dimIndex[dim.Id] = dims.Count;
            dims.Add(dim);
        }

        var conversions = new List<(string From, string To, double Factor, double Offset)>();
        if (unitsNode["conversions"] is JsonNode convNode)
        {
            foreach (var item in convNode.AsArray())
            {
                var obj = item!.AsObject();
                conversions.Add((obj["from"]!.GetValue<string>(), obj["to"]!.GetValue<string>(),
                    obj["factor"]!.GetValue<double>(), obj["offset"]?.GetValue<double>() ?? 0.0));
            }
        }

        var sampleNode = JsonNode.Parse(sample.RawJson)!.AsObject();
        var sampleValues = sampleNode["values"]!.AsObject();
        var rawSample = new JsonNode?[dims.Count];
        var normalizedSample = new double?[dims.Count];

        foreach (var dim in dims)
        {
            var idx = dimIndex[dim.Id];
            sampleValues.TryGetPropertyValue(dim.Id, out var valueNode);
            rawSample[idx] = valueNode?.DeepClone();
            if (valueNode == null || valueNode.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            {
                diagnostics.Add($"missing-isotope-dimension: {dim.Id} has no measured value; original null retained.");
                continue;
            }

            string valueUnit = dim.Unit;
            double value;
            if (valueNode.GetValueKind() == System.Text.Json.JsonValueKind.Object)
            {
                var obj = valueNode.AsObject();
                value = obj["value"]!.GetValue<double>();
                valueUnit = obj["unit"]!.GetValue<string>();
            }
            else
            {
                value = valueNode.GetValue<double>();
            }

            if (valueUnit == dim.Unit)
            {
                normalizedSample[idx] = value;
            }
            else
            {
                var conversion = conversions.FirstOrDefault(c => c.From == valueUnit && c.To == dim.Unit);
                if (conversion == default)
                {
                    diagnostics.Add(
                        $"unit-unconvertible: sample {dim.Id} recorded as '{valueUnit}' = {value}; no conversion to '{dim.Unit}', value kept at original position and dimension excluded from derivation.");
                }
                else
                {
                    normalizedSample[idx] = value * conversion.Factor + conversion.Offset;
                }
            }
        }

        var endmemberNode = JsonNode.Parse(endmembers.RawJson)!.AsObject();
        var endmemberList = new List<EndmemberNorm>();
        foreach (var item in endmemberNode["endmembers"]!.AsArray())
        {
            var obj = item!.AsObject();
            var id = obj["id"]!.GetValue<string>();
            var label = obj["label"]?.GetValue<string>() ?? id;
            var modes = new List<double[]>();
            foreach (var modeNode in obj["modes"]!.AsArray())
            {
                var mean = modeNode!.AsObject()["mean"]!.AsObject();
                var vector = new double[dims.Count];
                foreach (var dim in dims)
                {
                    if (mean.TryGetPropertyValue(dim.Id, out var v) && v!.GetValueKind() != System.Text.Json.JsonValueKind.Null)
                    {
                        vector[dimIndex[dim.Id]] = v.GetValue<double>();
                    }
                    else
                    {
                        diagnostics.Add($"missing-isotope-dimension: endmember {id} lacks {dim.Id}.");
                    }
                }
                modes.Add(vector);
            }
            endmemberList.Add(new EndmemberNorm(id, label, modes));
        }

        var covNode = JsonNode.Parse(covariance.RawJson)!.AsObject();
        var activeDims = dims.Where((d, i) => normalizedSample[i].HasValue).ToList();
        var sampleCov = BuildMatrix(activeDims, dimIndex, covNode["sample"]!.AsObject());
        var endmemberCov = new Dictionary<string, double[,]>();
        foreach (var endmember in endmemberList)
        {
            var map = covNode["endmembers"]?.AsObject()?[endmember.Id]?.AsObject();
            endmemberCov[endmember.Id] = map == null
                ? IdentityMatrix(activeDims.Count)
                : BuildMatrix(activeDims, dimIndex, map);
        }

        var fracNode = JsonNode.Parse(fractionation.RawJson)!.AsObject();
        var fractionationVersion = fracNode["version_tag"]?.GetValue<string>() ?? fractionation.Id;
        var offsets = new Dictionary<string, double[]>();
        var offsetMap = fracNode["offsets"]?.AsObject();
        foreach (var endmember in endmemberList)
        {
            var vector = new double[dims.Count];
            if (offsetMap != null && offsetMap[endmember.Id] is JsonNode perEndmember)
            {
                foreach (var dim in dims)
                {
                    if (perEndmember.AsObject().TryGetPropertyValue(dim.Id, out var v))
                    {
                        vector[dimIndex[dim.Id]] = v!.GetValue<double>();
                    }
                }
            }
            offsets[endmember.Id] = vector;
        }

        var priorNode = JsonNode.Parse(prior.RawJson)!.AsObject();
        var bounds = new (double, double)[endmemberList.Count];
        var priorBounds = priorNode["bounds"]?.AsObject();
        for (var i = 0; i < endmemberList.Count; i++)
        {
            if (priorBounds != null && priorBounds[endmemberList[i].Id] is JsonNode boundNode)
            {
                var array = boundNode.AsArray();
                bounds[i] = (array[0]!.GetValue<double>(), array[1]!.GetValue<double>());
            }
            else
            {
                bounds[i] = (0.0, 1.0);
            }
        }

        var deps = new List<Dependency>
        {
            new(units.Id, units.Version, units.Kind, units.ContentHash),
            new(sample.Id, sample.Version, sample.Kind, sample.ContentHash),
            new(endmembers.Id, endmembers.Version, endmembers.Kind, endmembers.ContentHash),
            new(covariance.Id, covariance.Version, covariance.Kind, covariance.ContentHash),
            new(fractionation.Id, fractionation.Version, fractionation.Kind, fractionation.ContentHash),
            new(prior.Id, prior.Version, prior.Kind, prior.ContentHash)
        };

        return new NormalizedInput(dims, rawSample, normalizedSample, diagnostics, endmemberList,
            sampleCov, endmemberCov, offsets, bounds, fractionationVersion, deps);
    }

    private static double[,] BuildMatrix(IReadOnlyList<DimInfo> dims, IReadOnlyDictionary<string, int> dimIndex, JsonObject map)
    {
        var matrix = new double[dims.Count, dims.Count];
        foreach (var rowDim in dims)
        {
            foreach (var colDim in dims)
            {
                if (map.TryGetPropertyValue(rowDim.Id, out var rowNode) &&
                    rowNode!.AsObject().TryGetPropertyValue(colDim.Id, out var valueNode))
                {
                    matrix[dims.IndexOf(rowDim), dims.IndexOf(colDim)] = valueNode!.GetValue<double>();
                }
            }
        }
        return matrix;
    }

    private static double[,] IdentityMatrix(int n)
    {
        var matrix = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            matrix[i, i] = 1.0;
        }
        return matrix;
    }
}

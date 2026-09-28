using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Domain;

public sealed class ArchiveManifest
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "isotope-archive/v1";
    [JsonPropertyName("package_id")] public string PackageId { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("coordinate_reference")] public string CoordinateReference { get; set; } = "VPDB/AIR per-dimension notation";
    [JsonPropertyName("dimensions")] public List<DimensionDef> Dimensions { get; set; } = new();
    [JsonPropertyName("samples")] public List<SampleDef> Samples { get; set; } = new();
    [JsonPropertyName("endmember_sets")] public List<EndmemberSetDef> EndmemberSets { get; set; } = new();
    [JsonPropertyName("measurement_covariances")] public List<CovarianceDef> MeasurementCovariances { get; set; } = new();
    [JsonPropertyName("fractionation_versions")] public List<FractionationDef> FractionationVersions { get; set; } = new();
    [JsonPropertyName("priors")] public List<PriorDef> Priors { get; set; } = new();
    [JsonPropertyName("assumptions")] public List<AssumptionDef> Assumptions { get; set; } = new();
    [JsonPropertyName("replay")] public List<ReplayStepDef> Replay { get; set; } = new();
}

public sealed class DimensionDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("standard")] public string Standard { get; set; } = "";
    [JsonPropertyName("order")] public int Order { get; set; }
}

public sealed class ValueRep
{
    [JsonPropertyName("raw")] public string Raw { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
}

public sealed class SampleDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("values")] public Dictionary<string, ValueRep> Values { get; set; } = new();
    [JsonPropertyName("covariance_id")] public string CovarianceId { get; set; } = "";
    [JsonPropertyName("order")] public int Order { get; set; }
}

public sealed class EndmemberDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("means")] public Dictionary<string, ValueRep> Means { get; set; } = new();
    [JsonPropertyName("variance")] public Dictionary<string, double> Variance { get; set; } = new();
    [JsonPropertyName("order")] public int Order { get; set; }
}

public sealed class EndmemberSetDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("endmembers")] public List<EndmemberDef> Endmembers { get; set; } = new();
}

public sealed class CovarianceEntry
{
    [JsonPropertyName("dimension")] public string Dimension { get; set; } = "";
    [JsonPropertyName("other")] public string Other { get; set; } = "";
    [JsonPropertyName("value")] public double Value { get; set; }
}

public sealed class CovarianceDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "permil^2";
    [JsonPropertyName("entries")] public List<CovarianceEntry> Entries { get; set; } = new();
}

public sealed class FractionationChannel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("offset")] public Dictionary<string, ValueRep> Offset { get; set; } = new();
}

public sealed class FractionationDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "single";
    [JsonPropertyName("channels")] public List<FractionationChannel> Channels { get; set; } = new();
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

public sealed class PriorConstraint
{
    [JsonPropertyName("endmember_id")] public string EndmemberId { get; set; } = "";
    [JsonPropertyName("op")] public string Op { get; set; } = ">=";
    [JsonPropertyName("value")] public double Value { get; set; }
}

public sealed class PriorDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("constraints")] public List<PriorConstraint> Constraints { get; set; } = new();
}

public sealed class AssumptionDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
}

public sealed class ReplayStepDef
{
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    [JsonPropertyName("target")] public string Target { get; set; } = "";
}

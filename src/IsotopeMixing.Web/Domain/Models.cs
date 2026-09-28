using System.Text.Json.Serialization;

namespace IsotopeMixing.Domain;

/// <summary>
/// A measured or reported quantity with its original textual representation preserved.
/// The derived layer converts to a canonical value; when conversion is impossible the
/// value stays in place (Canonical == null) with a diagnostic, never dropped for plotting.
/// </summary>
public sealed record Quantity(
    [property: JsonPropertyName("raw")] string Raw,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("canonical")] double? Canonical,
    [property: JsonPropertyName("canonicalUnit")] string? CanonicalUnit,
    [property: JsonPropertyName("diagnostics")] List<string> Diagnostics);

/// <summary>One isotope dimension. All coordinates need an explicit datum/reference.</summary>
public sealed record IsotopeAxis(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("datum")] string Datum,
    [property: JsonPropertyName("canonicalUnit")] string CanonicalUnit);

/// <summary>A sample measurement. Null value = missing isotope dimension (kept, not dropped).</summary>
public sealed record SampleMeasurement(
    [property: JsonPropertyName("axis")] string Axis,
    [property: JsonPropertyName("quantity")] Quantity? Quantity);

public sealed record Sample(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("measurements")] List<SampleMeasurement> Measurements,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <summary>Endmember source composition per isotope, with its own source covariance.</summary>
public sealed record EndmemberValue(
    [property: JsonPropertyName("axis")] string Axis,
    [property: JsonPropertyName("quantity")] Quantity? Quantity);

public sealed record Endmember(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("values")] List<EndmemberValue> Values);

/// <summary>A named version set of endmembers (source composition room).</summary>
public sealed record EndmemberSetVersion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("endmembers")] List<Endmember> Endmembers,
    /// <summary>Per-axis source standard deviations (canonical units). Covariance constructed from these + correlations.</summary>
    [property: JsonPropertyName("sourceSigmas")] Dictionary<string, double>? SourceSigmas = null,
    [property: JsonPropertyName("sourceCorrelation")] double SourceCorrelation = 0.0);

/// <summary>
/// A fractionation hypothesis version: offsets applied to endmember compositions
/// (mixing after fractionation model: mu_i = sum p_k(E_ki + f_ki)).
/// Offsets are in canonical units, keyed axis (shared), or by endmember+axis.
/// </summary>
public sealed record FractionationVersion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("offsetsByAxis")] Dictionary<string, double> OffsetsByAxis,
    [property: JsonPropertyName("offsetsByEndmemberAxis")] Dictionary<string, double>? OffsetsByEndmemberAxis = null,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <summary>
/// Measurement covariance for a sample. Rows/columns follow <see cref="Axes"/> order.
/// Matrix is stored row-major. Diagonal-only input is accepted; off-diagonal terms are
/// what marks "correlated errors".
/// </summary>
public sealed record MeasurementCovariance(
    [property: JsonPropertyName("sampleId")] string SampleId,
    [property: JsonPropertyName("axes")] List<string> Axes,
    [property: JsonPropertyName("matrix")] List<List<double>> Matrix);

/// <summary>Prior hypothesis on proportions. May be infeasible; that is a tested situation.</summary>
public sealed record PriorHypothesis(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("proportions")] Dictionary<string, double> Proportions,
    [property: JsonPropertyName("nonnegative")] bool Nonnegative = true,
    [property: JsonPropertyName("sumOne")] bool SumOne = true);

/// <summary>A complete, self-describing source composition room (raw records preserved).</summary>
public sealed record CaseDataset(
    [property: JsonPropertyName("datasetId")] string DatasetId,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("axes")] List<IsotopeAxis> Axes,
    [property: JsonPropertyName("samples")] List<Sample> Samples,
    [property: JsonPropertyName("endmemberSets")] List<EndmemberSetVersion> EndmemberSets,
    [property: JsonPropertyName("fractionations")] List<FractionationVersion> Fractionations,
    [property: JsonPropertyName("covariances")] List<MeasurementCovariance> Covariances,
    [property: JsonPropertyName("priors")] List<PriorHypothesis> Priors,
    [property: JsonPropertyName("rawBlobs")] List<RawBlob> RawBlobs);

/// <summary>Original imported byte/record content, kept verbatim and referenced by derived layers.</summary>
public sealed record RawBlob(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("contentBase64")] string ContentBase64,
    [property: JsonPropertyName("sha256")] string Sha256);

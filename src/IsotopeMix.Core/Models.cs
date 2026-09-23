namespace IsotopeMix.Core;

/// <summary>An isotope axis with its unit and coordinate reference standard.</summary>
public sealed record IsotopeAxis(string Id, string Unit, string Standard);

/// <summary>A measured isotope value that always carries unit + reference standard.</summary>
public sealed record IsotopeValue(string AxisId, double Value, string Unit, string Standard);

/// <summary>Raw import record: the original representation is never rewritten.</summary>
public sealed record RawRecord(string Id, string Kind, string MediaType, string Content, string? Note = null);

public sealed record SampleRecord(
    string Id,
    string Name,
    IReadOnlyList<IsotopeValue> Values,
    /// <summary>Full covariance (row-major, d x d) over the sample's own axis order; correlated errors kept.</summary>
    IReadOnlyList<double> Covariance,
    string RawRecordId);

public sealed record EndmemberRecord(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<IsotopeValue> Means,
    IReadOnlyList<double> Covariance,
    string RawRecordId);

public sealed record FractionationVersion(
    string Id,
    string Version,
    /// <summary>Per-axis offset (in axis unit) mapping endmember space into sample space.</summary>
    IReadOnlyDictionary<string, double> Offsets,
    string RawRecordId);

public sealed record PriorAssumption(
    string Id,
    string Name,
    /// <summary>Lower/upper bounds per endmember id, in proportion units (0..1).</summary>
    IReadOnlyDictionary<string, (double Lo, double Hi)> Bounds,
    string RawRecordId);

/// <summary>A feasible mixing-proportion candidate on the simplex.</summary>
public sealed record MixCandidate(
    string Id,
    string SampleId,
    IReadOnlyList<string> EndmemberIds,
    string EndmemberVersion,
    string FractionationVersion,
    string PriorId,
    IReadOnlyDictionary<string, double> Proportions,
    double MahalanobisDistance,
    bool Feasible,
    IReadOnlyList<string> Diagnostics);

/// <summary>Machine-readable judgement event, e.g. identifiability or constraint handling.</summary>
public sealed record JudgementEvent(
    string Kind,
    string Summary,
    IReadOnlyDictionary<string, string> Details);

namespace IsotopeLab.Mixing;

/// <summary>Parsed view of a source record. Normalised numeric values live next to the original unit label.</summary>
public sealed record DimensionDef(string Id, string Label, string? Unit, string? ReferenceScale, int Order);

public sealed record EndmemberMember(
    string SourceId,
    string Label,
    double[] Means,
    double[,]? Covariance,
    string? Unit,
    string? ReferenceScale);

public sealed record EndmemberSet(
    string RecordId,
    string VersionLabel,
    IReadOnlyList<DimensionDef> Dimensions,
    IReadOnlyList<EndmemberMember> Members,
    string? Unit,
    string? ReferenceScale,
    IReadOnlyList<string> Diagnostics);

public sealed record Sample(
    string RecordId,
    string Label,
    double?[] Values,
    string? Unit,
    string? ReferenceScale,
    IReadOnlyList<string> PresentDimensionIds,
    IReadOnlyList<string> MissingDimensionIds,
    IReadOnlyList<string> Diagnostics);

public sealed record CovarianceMatrix(
    string RecordId,
    double[,] Matrix,
    string? Unit,
    bool Correlated,
    IReadOnlyList<string> DimensionIds,
    IReadOnlyList<string> Diagnostics);

public sealed record FractionationMember(string SourceId, double[] Values);

public sealed record FractionationScheme(
    string RecordId,
    string VersionLabel,
    string Convention,
    IReadOnlyList<FractionationMember> Members,
    string? Unit,
    IReadOnlyList<string> Diagnostics);

public sealed record PriorBox(
    string RecordId,
    string Label,
    Dictionary<string, (double Min, double Max)> BoundsBySource,
    IReadOnlyList<string> Diagnostics);

public sealed record AnalysisRequest(
    string Label,
    Sample Sample,
    EndmemberSet Endmembers,
    CovarianceMatrix Covariance,
    FractionationScheme Fractionation,
    PriorBox? Prior);

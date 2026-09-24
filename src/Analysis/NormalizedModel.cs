namespace IsotopeMixing.Analysis;

public sealed record SampleModel(
    string MaterialId,
    string SampleId,
    string Label,
    double?[] Means,
    bool[] Present,
    string[] Units,
    double? Lon,
    double? Lat,
    string CoordRaw,
    string? CoordDiagnostic);

public sealed record EndmemberSourceModel(
    string SourceId, string Label,
    double?[] Means, bool[] Present, string[] Units);

public sealed record EndmemberSetModel(
    string MaterialId, string Group, string Stage,
    IReadOnlyList<EndmemberSourceModel> Sources);

public sealed record CovarianceModel(
    string MaterialId, string SampleId,
    double[][] Matrix, bool HasCorrelation);

public sealed record FractionationModel(
    string MaterialId, string VersionId,
    double[] Delta, string[] Units);

public sealed record PriorBoundsModel(
    string MaterialId, string VersionId,
    double?[] Lower, double?[] Upper);

using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

/// <summary>导入包的结构化模型（领域材料，不是通用表格）。</summary>
public sealed record CasePackage
{
    public required int SchemaVersion { get; init; } = 1;
    public required string CaseId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<DimensionSpecDto> Dimensions { get; init; }
    public required IReadOnlyList<RawSampleDto> Samples { get; init; }
    public required IReadOnlyList<RawEndmemberDto> Endmembers { get; init; }
    public required IReadOnlyList<FractionationDto> Fractionations { get; init; }
    public required IReadOnlyList<PriorDto> Priors { get; init; }
    public IReadOnlyList<RawMaterialDto>? Materials { get; init; }
    public IReadOnlyList<ReplayStepDto>? Replay { get; init; }
}

public sealed record DimensionSpecDto
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required string Unit { get; init; }
    public string? CoordinateDatum { get; init; }
}

public sealed record RawSampleDto
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public CovarianceDto? Covariance { get; init; }
    public string? RawText { get; init; }
}

public sealed record RawEndmemberDto
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<EndmemberModeDto> Modes { get; init; }
    public string? RawText { get; init; }
}

public sealed record EndmemberModeDto
{
    public required string ModeId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyDictionary<string, string> Means { get; init; }
    public CovarianceDto? Covariance { get; init; }
}

public sealed record CovarianceDto
{
    public required IReadOnlyList<string> Dimensions { get; init; }
    public required double[][] Matrix { get; init; }
    public string? Unit { get; init; }
    public string? RawText { get; init; }
}

public sealed record FractionationDto
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Offsets { get; init; }
    public string? Basis { get; init; }
    public string? RawText { get; init; }
}

public sealed record PriorDto
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyDictionary<string, double?[]> Bounds { get; init; }
    public string? Basis { get; init; }
    public string? RawText { get; init; }
}

public sealed record RawMaterialDto
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required string MediaType { get; init; }
    public required string ContentBase64 { get; init; }
}

public sealed record ReplayStepDto
{
    public required string Action { get; init; }
    public string? BranchName { get; init; }
    public string? FromBranch { get; init; }
    public string? SampleId { get; init; }
    public string? FractionationId { get; init; }
    public string? PriorId { get; init; }
    public string? AnalysisTitle { get; init; }
    public string? Decision { get; init; }
    public string? Note { get; init; }
    public RawSampleDto? SamplePatch { get; init; }
    public FractionationDto? FractionationPatch { get; init; }
    public string? TargetAnalysisId { get; init; }
}

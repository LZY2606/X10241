using System.Text.Json.Serialization;

namespace IsotopeMix.Domain;

/// <summary>带单位的量：原始表示始终保留，规范化只发生在派生层。</summary>
public sealed record Quantity(string Raw, double? Value, string Unit, string? CoordinateDatum, string? Diagnostic = null)
{
    public bool Convertible => Value is not null && Diagnostic is null;
}

public sealed record DimensionSpec(string Code, string Label, string Unit, string? CoordinateDatum = null);

public sealed record CovarianceBlock
{
    public required IReadOnlyList<string> Dimensions { get; init; }
    public required double[][] Matrix { get; init; }
    /// <summary>原始表示（例如 CSV 片段），来源层原样保存。</summary>
    public string? RawText { get; init; }
    public string? Unit { get; init; }
}

public sealed record RawSample
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyDictionary<string, Quantity> Values { get; init; }
    public CovarianceBlock? Covariance { get; init; }
    public string? RawText { get; init; }
}

public sealed record EndmemberMode
{
    public required string ModeId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyDictionary<string, Quantity> Means { get; init; }
    public CovarianceBlock? Covariance { get; init; }
}

public sealed record RawEndmember
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<EndmemberMode> Modes { get; init; }
    public string? RawText { get; init; }
}

/// <summary>分馏因子版本：每端元每维的分馏量（端元 -> 混合体）。</summary>
public sealed record FractionationSet
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    public required string Version { get; init; }
    /// <summary>offsets[endmemberId][dimension]，单位与同位素维一致。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Offsets { get; init; }
    public string? Basis { get; init; }
    public string? RawText { get; init; }
}

public sealed record PriorAssumption
{
    public required string StableId { get; init; }
    public required string Label { get; init; }
    /// <summary>每端元 [下限, 上限]；null 表示不约束。</summary>
    public required IReadOnlyDictionary<string, (double? Min, double? Max)> Bounds { get; init; }
    public string? Basis { get; init; }
    public string? RawText { get; init; }
}

public sealed record SolverOptions
{
    public int GridResolution { get; init; } = 41;
    public double ChiSquareLevel { get; init; } = 0.95;
    public double IdentifiabilityConditionThreshold { get; init; } = 30.0;
    public double NullspaceFractionThreshold { get; init; } = 0.12;
}

public sealed record FeasibleComponent
{
    public required int Index { get; init; }
    public required IReadOnlyList<double[]> Vertices { get; init; }
    public required double[] Centroid { get; init; }
    public required double Volume { get; init; }
    public required double[] BestFractions { get; init; }
    public required double BestChiSquare { get; init; }
    public required string ModeComboId { get; init; }
}

public sealed record FeasiblePoint
{
    public required double[] Fractions { get; init; }
    public required double ChiSquare { get; init; }
    public required double[] Residuals { get; init; }
    public required double[] StandardizedResiduals { get; init; }
    public required string ModeComboId { get; init; }
}

public sealed record SolverResult
{
    public required string Identifiability { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required IReadOnlyList<string> UsedDimensions { get; init; }
    public required IReadOnlyList<string> EndmemberOrder { get; init; }
    public required double ConditionNumber { get; init; }
    public required double[,] DesignMatrix { get; init; }
    public required IReadOnlyList<double[]> NullspaceDirections { get; init; }
    public required IReadOnlyList<FeasibleComponent> Components { get; init; }
    public required FeasiblePoint? BestPoint { get; init; }
    public required double ChiSquareCritical { get; init; }
    public required bool PriorInfeasible { get; init; }
    public required bool SampleOutsideEnvelope { get; init; }
    public required int GridResolution { get; init; }
    public required IReadOnlyDictionary<string, double> ParameterCovarianceAtBest { get; init; }
}

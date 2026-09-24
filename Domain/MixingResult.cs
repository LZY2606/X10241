namespace IsotopeMix.Domain;

public sealed record DimensionInfo(string AxisKey, bool Active, string? Reason);

public sealed record PolygonRing(IReadOnlyList<double[]> BarycentricPoints)
{
    public double[] Centroid
    {
        get
        {
            int n = BarycentricPoints.Count;
            var c = new double[BarycentricPoints[0].Length];
            foreach (var p in BarycentricPoints)
                for (int i = 0; i < c.Length; i++) c[i] += p[i] / n;
            return c;
        }
    }
}

public sealed record CandidateInfo(
    string CandidateId,
    string Label,
    IReadOnlyList<PolygonRing> Rings,
    double[] Centroid,
    double AreaFraction,
    double Chi2Minimum,
    bool ContainsPointEstimate,
    string? AdoptedByDecisionId);

public sealed record PointEstimate(
    double[] Proportions,
    double Chi2,
    bool Identified,
    bool RepresentativeOnly,
    double[]? LinearizedStdErrors,
    double KktResidual);

public sealed record ResidualInfo(
    string AxisKey, string Unit,
    double Observed, double Predicted,
    double RawResidual, double WhitenedComponent);

public sealed record EnvelopeInfo(bool Supported, bool? OutsideEnvelope, double? Margin);

public sealed record EngineEvent(
    string Code,
    string Severity,
    string Message,
    IReadOnlyDictionary<string, object?> Basis,
    IReadOnlyList<string> AffectedObjectIds);

public sealed record AnalysisResult
{
    public required string CaseId { get; init; }
    public required string SampleId { get; init; }
    public required string FractionationVersionId { get; init; }
    public required string SourceVersionId { get; init; }
    public required string PriorVersionId { get; init; }
    public required double ConfidenceLevel { get; init; }
    public required int DegreesOfFreedom { get; init; }
    public required double Chi2Threshold { get; init; }
    public required IReadOnlyList<DimensionInfo> Dimensions { get; init; }
    public required double[][] EffectiveSources { get; init; }
    public required double[] Observed { get; init; }
    public required EnvelopeInfo Envelope { get; init; }
    public required PointEstimate? Point { get; init; }
    public required IReadOnlyList<ResidualInfo> Residuals { get; init; }
    public required IReadOnlyList<CandidateInfo> Candidates { get; init; }
    public required IReadOnlyList<EngineEvent> Events { get; init; }
}

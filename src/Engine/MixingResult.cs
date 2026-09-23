using System.Collections.Generic;

namespace IsoMix.Engine;

public sealed record FeasiblePoint(double[] Fractions, double Mahalanobis2, int ComponentIndex, string ModeCombo);

public sealed record CandidateRegion(
    string CandidateId,
    int ComponentIndex,
    string ModeCombo,
    double[] BestFractions,
    double BestMahalanobis2,
    int GridPointCount,
    double[] MinFractions,
    double[] MaxFractions);

public sealed record MixingResult(
    bool Identifiable,
    string IdentifiabilityStatus,
    double ConditionNumber,
    int Rank,
    int SourceCount,
    int DimensionCount,
    bool SampleOutsideEnvelope,
    double EnvelopeGap,
    bool CorrelatedErrors,
    IReadOnlyList<string> MissingDimensions,
    bool MultipleFeasibleRegions,
    int RegionCount,
    bool PriorInfeasible,
    bool PriorBoundsInfeasible,
    double MahalanobisThreshold,
    double? BestMahalanobis2,
    double[]? BestFractions,
    double[]? Residual,
    double[][]? EffectiveCovarianceAtBest,
    IReadOnlyList<FeasiblePoint> FeasiblePoints,
    IReadOnlyList<CandidateRegion> Candidates,
    IReadOnlyList<EngineEvent> Events,
    double[][] SourceMeanMatrix,
    IReadOnlyList<string> SourceLabels,
    double[] SamplePoint,
    double[]? EnvelopeNearestPoint);

using System.Collections.Generic;
using System.Linq;
using IsoMix.Domain;

namespace IsoMix.App;

public sealed class DiagnosticDto
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Affected { get; set; } = new();
}

public sealed class ComponentDto
{
    public int Index { get; set; }
    public List<double[][]> BoundaryLoops { get; set; } = new();
    public double[]? Fractions { get; set; }
    public double MahalanobisSquared { get; set; }
    public double AreaFraction { get; set; }
}

public sealed class ResultDto
{
    public string HypothesisId { get; set; } = "";
    public List<string> Axes { get; set; } = new();
    public List<string> EndmemberIds { get; set; } = new();
    public List<string> EndmemberNames { get; set; } = new();
    public double[] Observation { get; set; } = System.Array.Empty<double>();
    public double[,] EndmemberMatrix { get; set; } = { };
    public bool Identifiable { get; set; }
    public double[] SingularValues { get; set; } = System.Array.Empty<double>();
    public double ConditionNumber { get; set; }
    public bool SampleInsideEnvelope { get; set; }
    public double EnvelopeMahalanobis { get; set; }
    public double[]? PointEstimate { get; set; }
    public bool Feasible { get; set; }
    public bool PriorInfeasible { get; set; }
    public List<ComponentDto> Components { get; set; } = new();
    public double[]? Residual { get; set; }
    public double? RmsResidual { get; set; }
    public double? BestMahalanobisSquared { get; set; }
    public double ChiSquareThreshold { get; set; }
    public double[]? GridF1 { get; set; }
    public double[]? GridF2 { get; set; }
    public double[,]? GridMahalanobis { get; set; }
    public bool[,]? GridFeasible { get; set; }
    public List<DiagnosticDto> Diagnostics { get; set; } = new();

    public static ResultDto Of(MixingResult r, string hypId) => new()
    {
        HypothesisId = hypId,
        Axes = r.Axes, EndmemberIds = r.EndmemberIds, EndmemberNames = r.EndmemberNames,
        Observation = r.Observation, EndmemberMatrix = r.EndmemberMatrix,
        Identifiable = r.Identifiable, SingularValues = r.SingularValues,
        ConditionNumber = r.ConditionNumber, SampleInsideEnvelope = r.SampleInsideEnvelope,
        EnvelopeMahalanobis = r.EnvelopeMahalanobis, PointEstimate = r.PointEstimate,
        Feasible = r.Feasible, PriorInfeasible = r.PriorInfeasible,
        Components = r.Components.Select(c => new ComponentDto
        {
            Index = c.Index,
            BoundaryLoops = c.BoundaryLoops,
            Fractions = c.Representative?.Fractions,
            MahalanobisSquared = c.Representative?.MahalanobisSquared ?? double.NaN,
            AreaFraction = c.AreaFraction
        }).ToList(),
        Residual = r.Residual, RmsResidual = r.RmsResidual,
        BestMahalanobisSquared = r.BestMahalanobisSquared, ChiSquareThreshold = r.ChiSquareThreshold,
        GridF1 = r.GridF1, GridF2 = r.GridF2, GridMahalanobis = r.GridMahalanobis, GridFeasible = r.GridFeasible,
        Diagnostics = r.Diagnostics.Select(d => new DiagnosticDto
        {
            Code = d.Code, Severity = d.Severity.ToString(), Message = d.Message, Affected = d.AffectedObjectIds
        }).ToList()
    };
}

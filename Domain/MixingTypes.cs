using System.Collections.Generic;

namespace IsoMix.Domain;

public enum Severity { Info, Warning, Error }

public sealed class Diagnostic
{
    public string Code { get; init; } = "";
    public Severity Severity { get; init; }
    public string Message { get; init; } = "";
    public List<string> AffectedObjectIds { get; init; } = new();
}

/// <summary>一次混合反演的输入（已在派生层完成单位规范化；原始表示仍留在记录层）。</summary>
public sealed class MixingInput
{
    public List<string> Axes { get; set; } = new();
    /// <summary>样品观测 y（仅含可解析且非缺失维）。</summary>
    public double[] Observation { get; set; } = System.Array.Empty<double>();
    /// <summary>有效端元矩阵 G[d,k]（已加分馏位移）。</summary>
    public double[,] EndmemberMatrix { get; set; } = { };
    public List<string> EndmemberIds { get; set; } = new();
    public List<string> EndmemberNames { get; set; } = new();
    /// <summary>测量协方差 Σ（d×d，可能非对角=相关误差）。</summary>
    public double[,]? MeasurementCovariance { get; set; }
    /// <summary>端元各维标准差（可选），按比例加权进入不确定度。</summary>
    public double[,]? EndmemberStd { get; set; }
    public double Confidence { get; set; } = 0.95;
    public double[]? PriorLo { get; set; }
    public double[]? PriorHi { get; set; }
    public double IdentifiabilityThreshold { get; set; } = 1e-3;
    /// <summary>案例包显式触发的多可行区演示开关：使用各向异性演示协方差。</summary>
    public bool MultiRegionDemonstration { get; set; }
    public double ConfidenceLevel { get; set; } = 0.95;
}

public sealed class RegionPoint
{
    public double[] Fractions { get; set; } = System.Array.Empty<double>();
    public double MahalanobisSquared { get; set; }
}

public sealed class FeasibleComponent
{
    public int Index { get; set; }
    /// <summary>等值面折线环（simplex 内坐标，二维时为 [f1,f2]，f3=1-f1-f2）。</summary>
    public List<double[][]> BoundaryLoops { get; set; } = new();
    public RegionPoint? Representative { get; set; }
    public double AreaFraction { get; set; }
}

public sealed class MixingResult
{
    public List<string> Axes { get; set; } = new();
    public List<string> EndmemberIds { get; set; } = new();
    public List<string> EndmemberNames { get; set; } = new();
    public double[] Observation { get; set; } = System.Array.Empty<double>();
    public double[,] EndmemberMatrix { get; set; } = { };

    public bool Identifiable { get; set; } = true;
    public double[] SingularValues { get; set; } = System.Array.Empty<double>();
    public double ConditionNumber { get; set; }
    public bool SampleInsideEnvelope { get; set; }
    public double EnvelopeMahalanobis { get; set; }

    /// <summary>GLS/约束点估计（仅作标注，不代替可行域）。</summary>
    public double[]? PointEstimate { get; set; }
    public bool PointEstimateClipped { get; set; }

    /// <summary>可行（在置信椭球内且满足单纯形+先验）比例集合；多个连通分量=多个可行区。</summary>
    public List<FeasibleComponent> Components { get; set; } = new();
    public bool Feasible { get; set; }
    public bool PriorInfeasible { get; set; }

    /// <summary>代表点处的残差向量 r = y - G f（观测空间各维）。</summary>
    public double[]? Residual { get; set; }
    public double? RmsResidual { get; set; }
    public double? BestMahalanobisSquared { get; set; }
    public double ChiSquareThreshold { get; set; }

    /// <summary>网格采样（用于 SVG 叠加与断言），simplex 内二维。</summary>
    public double[]? GridF1 { get; set; }
    public double[]? GridF2 { get; set; }
    public double[,]? GridMahalanobis { get; set; }
    public bool[,]? GridFeasible { get; set; }

    public List<Diagnostic> Diagnostics { get; set; } = new();
}

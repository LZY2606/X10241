namespace IsotopeMix.Core.Mixing;

[Flags]
public enum MixingFlags
{
    None = 0,
    Underidentified = 1,        // 端元近共线/维度不足：无法唯一决定
    OutsideEnvelope = 2,        // 样品超出端元包络（即使考虑误差也无解）
    InfeasiblePrior = 4,        // 先验盒与单纯形不相交
    MissingDimension = 8,       // 样品缺失至少一个同位素维
    MultipleFeasibleRegions = 16, // 多个互不连通的合法可行区
    DistributionalEndmembers = 32,
    FractionationApplied = 64
}

public sealed record FeasibleCandidate(
    string Id,
    string RegionId,
    double[] Fractions,
    double ChiSquare,
    double[] Residuals);

public sealed record FeasibleRegion(
    string Id,
    List<(double U, double V)> Cells,   // 三角形重心坐标网格上的可行格点
    double[] Centroid,
    double[] BestFractions,
    double BestChiSquare,
    List<FeasibleCandidate> Candidates);

/// <summary>一次混合推导的完整结果（可序列化持久化，支持历史回放）。</summary>
public sealed class MixingResult
{
    public string RunId { get; set; } = "";
    public string RuleVersion { get; set; } = "";
    public string SampleId { get; set; } = "";
    public string EndmemberVersion { get; set; } = "";
    public string FractionationVersion { get; set; } = "";
    public string CovarianceId { get; set; } = "";
    public string PriorId { get; set; } = "";

    public List<string> EndmemberLabels { get; set; } = new();
    public List<string> Dimensions { get; set; } = new();
    public List<string> MissingDimensions { get; set; } = new();
    public double[,] DesignMatrix { get; set; } = { };
    public double[] Observed { get; set; } = Array.Empty<double>();
    public double[,] TotalCovariance { get; set; } = { };
    public double[,]? SampleCovariance { get; set; }
    public double[,]? EndmemberCovariance { get; set; }

    public double ChiThreshold { get; set; }
    public double ConfidenceLevel { get; set; } = 0.95;
    public MixingFlags Flags { get; set; }
    public double SingularRatio { get; set; }
    public string? Diagnosis { get; set; }
    public List<string> Diagnostics { get; set; } = new();

    public double[]? UnconstrainedFit { get; set; }
    public double[]? ConstrainedBestFit { get; set; }
    public double[]? BestResiduals { get; set; }
    public double? BestChiSquare { get; set; }

    public List<FeasibleRegion> Regions { get; set; } = new();
    public int GridSubdivisions { get; set; }
    public bool PriorFeasible { get; set; } = true;

    public bool IsFeasible => Regions.Count > 0;
}

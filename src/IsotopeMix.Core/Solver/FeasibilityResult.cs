using System.Collections.Generic;

namespace IsotopeMix.Core.Solver;

public enum FeasibilityStatus
{
    Unique = 0,        // 可行域退化为点（理论情形）
    Feasible = 1,      // 存在非退化可行域（可能不可识别）
    Infeasible = 2,    // 椭球 ∩ simplex ∩ 先验为空
    NotEvaluable = 3   // 维度/数据不足，无法判定
}

// 机器可读的判定依据事件（与存储层 FindingEvent 对齐）。
public sealed class Finding
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "info"; // info/warning/error
    public string Message { get; set; } = "";
    public Dictionary<string, object> Evidence { get; set; } = new();
}

public sealed class FeasibilityResult
{
    public string SampleId { get; set; } = "";
    public List<string> EndmemberIds { get; set; } = new();
    public List<string> ActiveTracers { get; set; } = new();
    public FeasibilityStatus Status { get; set; }
    public bool Identifiable { get; set; } = true;
    public List<Finding> Findings { get; set; } = new();
    // 二维（比例空间）可行多边形顶点；空集表示不可行，单点表示唯一。
    public List<double[]> PolygonVertices { get; set; } = new();
    public double?[] RepresentativeProportions { get; set; } = System.Array.Empty<double?>();
    // 端元坐标处的残差向量（按 active tracers），单位为规范化 δ。
    public double[]? ResidualVector { get; set; }
    public double? MahalanobisSquared { get; set; }
    public double? EnvelopeMahalanobisSquared { get; set; } // 包络上最近点距离，超包络时 > 阈值
    public bool WithinEnvelope { get; set; } = true;
    // 多个合法候选点（不可唯一决定时保留全部合法候选，不做单点伪造）。
    public List<double[]> CandidateProportions { get; set; } = new();
    public double[,]? CovarianceUsed { get; set; }
    public bool CorrelatedErrorsUsed { get; set; }
    public double ConfidenceLevel { get; set; } = 0.95;
}

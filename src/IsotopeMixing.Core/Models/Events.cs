namespace IsotopeMixing.Core.Models;

/// <summary>
/// 事件溯源：分支上的每个事实都是一条不可变事件。
/// 回到任意旧事件即可恢复当时的依赖版本与可见结论。
/// </summary>
public class BranchEvent
{
    public long Seq { get; set; }
    public string EventId { get; set; } = "";       // 内容确定的稳定身份
    public string BranchId { get; set; } = "";
    public string ParentEventId { get; set; } = ""; // 分支内前一事件（链）
    public string Type { get; set; } = "";
    /// <summary>record.added | analysis.requested | analysis.recorded |
    /// candidate.adjudicated | branch.forked | branch.undone | parameter.revised | source.changed</summary>
    public string PayloadJson { get; set; } = "";
    public string At { get; set; } = "";
}

public class BranchInfo
{
    public string BranchId { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ParentBranchId { get; set; } = "";
    public string ParentEventId { get; set; } = "";  // fork 起点
    public long HeadSeq { get; set; }
    public string HeadEventId { get; set; } = "";
    public bool Retired { get; set; }
}

/// <summary>一次混合分析请求所钉住的依赖版本（绝不被全局替换）。</summary>
public class AnalysisRequest
{
    public string AnalysisId { get; set; } = "";
    public string SampleRecordId { get; set; } = "";
    public string SampleVersion { get; set; } = "";
    public List<RefVersion> Endmembers { get; set; } = new();
    public RefVersion? Covariance { get; set; }
    public RefVersion Fractionation { get; set; } = new();
    public RefVersion? Prior { get; set; }
    public string Confidence { get; set; } = "0.05";
}

public class RefVersion
{
    public string RecordId { get; set; } = "";
    public string Version { get; set; } = "";
}

/// <summary>可识别性 / 可行域分析结果。</summary>
public class AnalysisResult
{
    public string AnalysisId { get; set; } = "";
    public string Status { get; set; } = "";        // identifiable|weakly_identifiable|unidentifiable|infeasible|indeterminate
    public bool Feasible { get; set; }
    public double? ChiSquareMin { get; set; }
    public double? ChiSquareThreshold { get; set; }
    public int DoF { get; set; }
    public double?[] PointEstimate { get; set; } = Array.Empty<double?>();
    public List<FeasibleComponent> Components { get; set; } = new();
    public List<DiagnosticCode> Diagnostics { get; set; } = new();
    public double[]? Residuals { get; set; }
    public List<string> ActiveTracers { get; set; } = new();
    public string ComputedAt { get; set; } = "";
}

public class FeasibleComponent
{
    public string ComponentId { get; set; } = "";
    public List<double[]?> Vertices { get; set; } = new(); // simplex 顶点（null 表示缺失方向上自由）
    public double MinSpan { get; set; }
    public double[]? PointEstimate { get; set; }
}

public class DiagnosticCode
{
    public string Code { get; set; } = "";          // S1_NEAR_COLLINEAR 等
    public string Severity { get; set; } = "info";  // info|warning|error
    public string Message { get; set; } = "";
    public Dictionary<string, string> Evidence { get; set; } = new();
}

/// <summary>依赖失效范围：参数/源数据变更只使受影响分析失效。</summary>
public class InvalidationScope
{
    public string ChangedRecordId { get; set; } = "";
    public string OldVersion { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public List<string> AffectedAnalysisIds { get; set; } = new();
}

public class AdjudicationPayload
{
    public string AnalysisId { get; set; } = "";
    public string ComponentId { get; set; } = "";
    public string Decision { get; set; } = "";      // accepted|rejected|adjusted
    public string Rationale { get; set; } = "";
    public double[]? AdjustedProportions { get; set; }
    public string SupersedesAdjudicationId { get; set; } = "";
}

public class AdjudicationRecord
{
    public string AdjudicationId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public long EventSeq { get; set; }
    public string EventId { get; set; } = "";
    public string AnalysisId { get; set; } = "";
    public string ComponentId { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Rationale { get; set; } = "";
    public double[]? AdjustedProportions { get; set; }
    public string UndoneByEventId { get; set; } = "";
    public string Status => string.IsNullOrEmpty(UndoneByEventId) ? Decision : "undone";
}

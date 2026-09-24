using System.Collections.Generic;

namespace IsotopeMixing.Core.Pack;

// 离线案例包 DTO（JSON）。这是“源组成室”领域材料，不是通用表格：
// 维度带单位与坐标基准；所有数值带原始文本；端元支持多峰分布；
// 分馏、先验、协方差、重放记录均显式成节。
public sealed record CasePack(
    string CaseId,
    string Title,
    string Description,
    IReadOnlyList<PackDimension> Dimensions,
    IReadOnlyList<PackSample> Samples,
    IReadOnlyList<PackEndmemberSet> EndmemberSets,
    IReadOnlyList<PackCovariance> Covariances,
    IReadOnlyList<PackFractionation> Fractionations,
    IReadOnlyList<PackPrior> Priors,
    IReadOnlyList<PackBranch> BaselineBranches,
    IReadOnlyList<ReplayStep> ReplayLog);

public sealed record PackDimension(string Id, string Label, string Unit, string ReferenceScale);

public sealed record PackSampleValue(string Dimension, string RawValue);
public sealed record PackSampleUnit(string Dimension, string RawUnit);
public sealed record PackSample(
    string Id, string Label,
    IReadOnlyList<PackSampleValue> Values,
    IReadOnlyList<PackSampleUnit> Units,
    string? Provenance);

public sealed record PackMode(
    string ModeId,
    IReadOnlyList<string?> Means,
    IReadOnlyList<string> StdDevs,
    double Weight);

public sealed record PackEndmember(
    string Id, string Label,
    IReadOnlyList<PackSampleValue> Means,
    IReadOnlyList<PackSampleValue> StdDevs,
    IReadOnlyList<PackMode>? Modes,
    string? Provenance);

public sealed record PackEndmemberSet(
    string Id, string Label, string? Parent,
    IReadOnlyList<PackEndmember> Endmembers,
    IReadOnlyList<string> DimensionOrder,
    string ChangeNote);

public sealed record PackCovariance(
    string Id, string Label, string SampleId,
    IReadOnlyList<string> DimensionOrder,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? RawRows);

public sealed record PackFractionationFactor(string Endmember, string Dimension, string RawValue);
public sealed record PackFractionation(
    string Id, string Label, string? Parent,
    IReadOnlyList<PackFractionationFactor> Factors,
    string ChangeNote);

public sealed record PackPriorInterval(string Endmember, string? Min, string? Max);
public sealed record PackPrior(
    string Id, string Label, string Kind,
    IReadOnlyList<PackPriorInterval> Intervals,
    IReadOnlyDictionary<string, double>? Dirichlet,
    string RawRepresentation);

public sealed record PackBranch(
    string BranchId, string Label, string? Parent,
    string SampleId, string EndmemberSet, string Fractionation,
    string Covariance, string Prior);

// 重放记录：导入后审阅者依次触发的边界情形与裁定，包含相互冲突的人工决定。
public sealed record ReplayStep(
    int Seq,
    string Scenario,
    string BranchId,
    string? Action,
    string? CandidateEndmemberSet,
    string? CandidateFractionation,
    string? Decision,
    string? Note);

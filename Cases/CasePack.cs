using System.Collections.Generic;

namespace IsoMix.Cases;

/// <summary>
/// 仓库自带离线案例包“源组成室：同位素端元、分馏假设与可行混合区间”。
/// 这是领域材料：样品/端元/测量协方差/分馏版本/单位/先验均保留原始表示，
/// 另含固定边界情形与一条可重放叙事。
/// </summary>
public sealed class CasePack
{
    public string Schema { get; set; } = "isomix.casepack/1";
    public string PackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Narrative { get; set; } = "";
    public string RuleVersion { get; set; } = "";
    public string Confidence { get; set; } = "0.95";
    public List<AxisSpec> Axes { get; set; } = new();
    public List<SampleSpec> Samples { get; set; } = new();
    public List<EndmemberSetSpec> EndmemberSets { get; set; } = new();
    public List<CovarianceSpec> Covariances { get; set; } = new();
    public List<FractionationSpec> Fractionations { get; set; } = new();
    public List<PriorSpec> Priors { get; set; } = new();
    public List<HypothesisSpec> Hypotheses { get; set; } = new();
    public List<ScenarioSpec> Scenarios { get; set; } = new();
    public ReplaySpec Replay { get; set; } = new();
}

public sealed class AxisSpec
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Datum { get; set; } = "";
}

public sealed class RawNumber
{
    public string Raw { get; set; } = "";
    public string? Unit { get; set; }
}

public sealed class SampleSpec
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, RawNumber> Values { get; set; } = new();
    public List<string> MissingAxes { get; set; } = new();
    public string? Covariance { get; set; }
    public string? Note { get; set; }
}

public sealed class EndmemberSpec
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, RawNumber> Composition { get; set; } = new();
    public Dictionary<string, double> StdDev { get; set; } = new();
    public string? Note { get; set; }
}

public sealed class EndmemberSetSpec
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<EndmemberSpec> Members { get; set; } = new();
}

public sealed class CovarianceSpec
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> AxisOrder { get; set; } = new();
    public List<List<string>> Matrix { get; set; } = new();
    public string? Unit { get; set; }
}

public sealed class FractionationFactorSpec
{
    public string Axis { get; set; } = "";
    public string? Endmember { get; set; }
    public string Delta { get; set; } = "";
    public string Unit { get; set; } = "";
}

public sealed class FractionationSpec
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<FractionationFactorSpec> Factors { get; set; } = new();
    public string? Note { get; set; }
}

public sealed class PriorSpec
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, string> Lo { get; set; } = new();
    public Dictionary<string, string> Hi { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>一个分析假设：选用哪套端元、哪版分馏、哪条先验。</summary>
public sealed class HypothesisSpec
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Sample { get; set; } = "";
    public string EndmemberSet { get; set; } = "";
    public string Fractionation { get; set; } = "";
    public string? Prior { get; set; }
    public List<string> MissingAxes { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>固定边界情形：在基线上叠加一个“只改动一个直接来源”的分支并给出断言。</summary>
public sealed class ScenarioSpec
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string ChangeKind { get; set; } = "";
    public string? Hypothesis { get; set; }
    public string? AmendRecord { get; set; }
    public string? AmendJson { get; set; }
    public List<string> ExpectDiagnosticCodes { get; set; } = new();
    public bool? ExpectIdentifiable { get; set; }
    public bool? ExpectFeasible { get; set; }
    public int? ExpectComponentCount { get; set; }
    public List<string> ExpectAffectedContains { get; set; } = new();
}

public sealed class ReplaySpec
{
    public List<ReplayStepSpec> Steps { get; set; } = new();
}

public sealed class ReplayStepSpec
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Record { get; set; }
    public string? PayloadJson { get; set; }
    public string? Hypothesis { get; set; }
    public string? Decision { get; set; }
}

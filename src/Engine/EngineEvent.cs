using System.Collections.Generic;

namespace IsoMix.Engine;

/// <summary>
/// 机器可读取判定事件。ruleId 稳定指向规则；severity/affectedIds 限定影响范围；
/// evidence 给出数值依据，便于断言与界面读回。
/// </summary>
public sealed record EngineEvent(
    string EventCode,
    string RuleId,
    string Severity,
    string Message,
    IReadOnlyDictionary<string, object?> Evidence,
    IReadOnlyList<string> AffectedIds);

public static class RuleIds
{
    public const string NearCollinear = "identifiability.near-collinear.v1";
    public const string Underdetermined = "identifiability.structural-underdetermined.v1";
    public const string Identifiable = "identifiability.ok.v1";
    public const string OutsideEnvelope = "envelope.sample-outside.v1";
    public const string CorrelatedError = "error.correlated-covariance-propagation.v1";
    public const string MissingDimension = "dimension.missing.v1";
    public const string FractionationSwitch = "fractionation.version-switch.v1";
    public const string MultipleRegions = "region.multiple-feasible-components.v1";
    public const string InfeasiblePrior = "prior.infeasible-intersection.v1";
    public const string ConstraintsHonest = "constraints.simplex-no-clamp-normalize.v1";
    public const string FractionNonnegative = "constraints.fraction-nonnegative.v1";
    public const string FractionSumOne = "constraints.fraction-sum-one.v1";
}

using System.Collections.Generic;
using IsotopeMixing.Core.Domain;

namespace IsotopeMixing.Core.Engine;

// 引擎入参：全部来自已保存的来源记录（样品 / 端元版本 / 协方差 / 分馏版本 / 先验）。
public sealed record EngineRequest(
    SampleRecord Sample,
    EndmemberSetVersion EndmemberSet,
    MeasurementCovariance? Covariance,
    FractionationVersion Fractionation,
    PriorAssumption Prior,
    double Confidence = 0.95,
    int? GridResolution = null);

internal sealed record ResolvedMode(int EndmemberIndex, int ModeIndex, double[] Mean, double[] Variance, double Weight);
internal sealed record ModeCombo(IReadOnlyList<ResolvedMode> Modes, int Key, double ComboWeight);
internal sealed record FeasiblePoint(double[] Weights, double MahalanobisSq, int ComboKey, int RegionIndex);

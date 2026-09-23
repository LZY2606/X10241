namespace IsotopeMixing.Cases;

public sealed class CaseFile
{
    public string CaseId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Domain { get; set; } = "";
    public string ConfidenceLevel { get; set; } = "0.95";
    public List<AxisFile> Axes { get; set; } = new();
    public List<EndmemberFile> Endmembers { get; set; } = new();
    public List<EndmemberSetFile> EndmemberSets { get; set; } = new();
    public List<SampleFile> Samples { get; set; } = new();
    public List<CovarianceFile> Covariances { get; set; } = new();
    public List<FractionationFile> Fractionations { get; set; } = new();
    public List<PriorFile> Priors { get; set; } = new();
    public List<ScenarioFile> Scenarios { get; set; } = new();
    public List<ReplayStepFile> Replay { get; set; } = new();
}

public sealed class AxisFile
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Datum { get; set; } = "";
}

public sealed class EndmemberFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, double> Means { get; set; } = new();
    public Dictionary<string, double> Sigma { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>>? Correlations { get; set; }
}

public sealed class EndmemberSetFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> Members { get; set; } = new();
}

public sealed class SampleFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, double?> Values { get; set; } = new();
    public string UnitsMode { get; set; } = "native"; // native | mixed
    public Dictionary<string, string>? UnitOverrides { get; set; }
    public string? Covariance { get; set; }
}

public sealed class CovarianceFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    // Full matrix in axis order; null cells allowed to mark missing dimensions.
    public List<List<double?>> Matrix { get; set; } = new();
    public List<string> Axes { get; set; } = new();
}

public sealed class FractionationFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public Dictionary<string, Dictionary<string, double>> Offsets { get; set; } = new();
}

public sealed class PriorFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public List<List<int>>? StructuralGroups { get; set; }
    public Dictionary<int, double[]>? WeightBoxes { get; set; }
    public string Note { get; set; } = "";
}

public sealed class ScenarioFile
{
    public string Uid { get; set; } = "";
    public string Label { get; set; } = "";
    public string Sample { get; set; } = "";
    public string EndmemberSet { get; set; } = "";
    public string? Fractionation { get; set; }
    public string? Prior { get; set; }
    public string? Covariance { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed class ReplayStepFile
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Branch { get; set; }
    public string? ParentBranch { get; set; }
    public string? Scenario { get; set; }
    public string? Result { get; set; }
    public string? Verdict { get; set; }
    public string? Rationale { get; set; }
    public string? Target { get; set; }
    public string? Fractionation { get; set; }
    public string? Prior { get; set; }
    public bool? Conflict { get; set; }
}

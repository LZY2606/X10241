using System.Text.Json.Serialization;

namespace IsotopeMix.Model;

// A single imported scalar: the original textual representation is always retained
// alongside the parsed value. Normalization happens only in derived layers.
public sealed class RawCell
{
    public string Raw { get; set; } = "";
    public double? Parsed { get; set; }
    public string Unit { get; set; } = "";
    public string Basis { get; set; } = "";
    public bool Convertible { get; set; } = true;
    public string? Diagnostic { get; set; }

    public static RawCell Unconvertible(string raw, string unit, string basis, string diagnostic) =>
        new() { Raw = raw, Parsed = null, Unit = unit, Basis = basis, Convertible = false, Diagnostic = diagnostic };
}

public sealed class CovData
{
    public List<string> Axes { get; set; } = new();
    public List<List<RawCell>> Rows { get; set; } = new();
    public string CorrelatedNote { get; set; } = "";
    public string RawFileName { get; set; } = "";
}

public sealed class SampleDoc
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public Dictionary<string, RawCell> Measurements { get; set; } = new();
    public CovData? Covariance { get; set; }
}

public sealed class SourceDoc
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Group { get; set; } = "";
    public Dictionary<string, RawCell> Means { get; set; } = new();
    public CovData? Covariance { get; set; }
}

// A fractionation version adjusts endmember means per source/isotope axis,
// carrying its own uncertainty and the convention it was recorded in.
public sealed class FractionationDoc
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Version { get; set; } = "";
    public string Convention { get; set; } = "";
    public Dictionary<string, Dictionary<string, RawCell>> Factors { get; set; } = new();
    public Dictionary<string, Dictionary<string, RawCell>> Uncertainty { get; set; } = new();
}

// Prior assumptions live as explicit, versioned records. Support sets can be
// unions of intervals (gap priors) or fixed points used to test feasibility.
public sealed class PriorDoc
{
    public string StableId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = ""; // dirichlet | gap | fixed
    public string? ScopeSampleId { get; set; }
    public Dictionary<string, double> DirichletAlpha { get; set; } = new();
    public List<SupportRegion> Support { get; set; } = new();
    public Dictionary<string, double> Fixed { get; set; } = new();
    public string Note { get; set; } = "";
}

public sealed class SupportRegion
{
    public Dictionary<string, double> Min { get; set; } = new();
    public Dictionary<string, double> Max { get; set; } = new();
}

public sealed class UnitDeclaration
{
    public string Code { get; set; } = "";
    public string Kind { get; set; } = ""; // isotope | proportion | dimensionless
    public string Basis { get; set; } = "";
    public string RawNote { get; set; } = "";
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace IsoMix.Core.Domain;

/// <summary>
/// A value exactly as it arrived in an import, with its unit and coordinate/reference
/// basis. The normalized canonical value (per-mille on the VPDB-style reference) is
/// derived on a separate layer; when a value cannot be converted it stays here,
/// keeps its original representation and carries a diagnostic instead of being
/// silently dropped.
/// </summary>
public sealed record Quantity(
    string RawText,
    double? Value,
    string Unit,
    string Reference,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsUsable => Value.HasValue && Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public double CanonicalValue => Value!.Value;

    public static Quantity Of(double value, string unit = "permil", string reference = "VPDB")
        => new(value.ToString("R"), value, unit, reference, Array.Empty<Diagnostic>());
}

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(string Code, string Message, DiagnosticSeverity Severity);

/// <summary>Measurement covariance between two isotope dimensions (per-mille squared).</summary>
public sealed record CovarianceCell(
    string RowIsotope,
    string ColIsotope,
    double VarianceOrCovariance,
    string Unit = "permil^2");

/// <summary>
/// A prior hypothesis about mixture proportions. Stored as an original record (with
/// unit labels and provenance) and checked strictly in the solver: an infeasible
/// prior is reported, never projected into feasibility by truncation.
/// </summary>
public sealed record PriorAssumption(
    string PriorId,
    string SampleId,
    IReadOnlyList<PriorComponent> Components,
    string Basis,
    string RawText);

public sealed record PriorComponent(string SourceId, double Weight);

public sealed record RawDocument(
    string RawId,
    string Kind,
    string FileName,
    string MediaType,
    byte[] Bytes,
    string Sha256,
    string ImportedAt)
{
    public string AsUtf8() => System.Text.Encoding.UTF8.GetString(Bytes);
}

/// <summary>
/// A measured sample. Missing isotope dimensions are represented by absence (plus an
/// explicit diagnostic), and values that cannot be converted stay as unusable
/// <see cref="Quantity"/> records instead of being cleaned away.
/// </summary>
public sealed record SampleRecord(
    string SampleId,
    string Material,
    IReadOnlyList<MeasuredIsotope> Isotopes,
    IReadOnlyList<CovarianceCell> Covariance,
    IReadOnlyList<Diagnostic> Diagnostics,
    string RawId,
    string JsonPointer);

public sealed record MeasuredIsotope(
    string Isotope,
    Quantity Quantity,
    bool Missing,
    string RawJsonPointer);

/// <summary>
/// One immutable version of a source/endmember distribution. Endmembers may be
/// mixtures themselves, so each source carries one or more weighted components
/// (a distribution over isotope space). A revision creates a new version with a
/// new stable id; old versions stay readable.
/// </summary>
public sealed record EndmemberSetVersion(
    string EndmemberSetId,
    string VersionId,
    string Label,
    IReadOnlyList<SourceDistribution> Sources,
    string RawId,
    string JsonPointer,
    string CreatedAt,
    string? SupersedesVersionId);

public sealed record SourceDistribution(
    string SourceId,
    string Name,
    IReadOnlyList<SourceComponent> Components);

public sealed record SourceComponent(
    string ComponentId,
    double Weight,
    IReadOnlyDictionary<string, Quantity> Means,
    IReadOnlyList<CovarianceCell> Covariance);

/// <summary>
/// One version of the fractionation rule set.
/// Convention "delta_shift" applies <c>shift = factor</c>; "multiply" applies
/// <c>shift = factor * sourceMean</c>. Switching version creates a new version id
/// and invalidates only analyses that depended on the previous version.
/// </summary>
public sealed record FractionationVersion(
    string FractionationId,
    string VersionId,
    string Label,
    string Convention,
    IReadOnlyList<FractionationFactor> Factors,
    string RawId,
    string JsonPointer,
    string CreatedAt,
    string? SupersedesVersionId);

public sealed record FractionationFactor(
    string SourceId,
    string Isotope,
    double Factor,
    string Unit);

public static class UnitCatalog
{
    public const string Permil = "permil";
    public const string Permyriad = "permyriad";
    public const string DeltaNotation = "delta";

    static readonly HashSet<string> PermilTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "permil", "permille", "\u2030", "ppt-permil", "\u03b4-permil", "delta_permil"
    };

    static readonly HashSet<string> PeroneToken = new(StringComparer.OrdinalIgnoreCase)
    {
        "permyriad", "\u2031"
    };

    /// <summary>
    /// Parses a raw value/unit pair into a <see cref="Quantity"/>. No irreversible
    /// cleaning happens: unparseable/unknown units produce an unusable quantity
    /// carrying a diagnostic and the original text.
    /// </summary>
    public static Quantity Parse(string? rawText, string? unit, string? reference)
    {
        var diags = new List<Diagnostic>();
        unit = (unit ?? "").Trim();
        reference = string.IsNullOrWhiteSpace(reference) ? "unspecified" : reference.Trim();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new Quantity("", null, unit, reference,
                new[] { new Diagnostic("Q.MISSING", "Value missing at import.", DiagnosticSeverity.Warning) });
        }

        var text = rawText!.Trim();
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var numeric))
        {
            return new Quantity(text, null, unit, reference,
                new[] { new Diagnostic("Q.UNPARSEABLE",
                    $"Value '{text}' could not be parsed; original representation retained.",
                    DiagnosticSeverity.Error) });
        }

        double canonical = numeric;
        if (PermilTokens.Contains(unit) || unit == "")
        {
            if (unit == "")
            {
                diags.Add(new Diagnostic("Q.UNIT_INFERRED",
                    "No unit given; per-mille assumed (original text retained).",
                    DiagnosticSeverity.Warning));
            }
        }
        else if (PeroneToken.Contains(unit))
        {
            canonical = numeric / 10.0;
            diags.Add(new Diagnostic("Q.UNIT_CONVERTED",
                "Converted per-myriad to canonical per-mille (raw retained).",
                DiagnosticSeverity.Info));
        }
        else
        {
            return new Quantity(text, null, unit, reference,
                new[] { new Diagnostic("Q.UNIT_UNKNOWN",
                    $"Unit '{unit}' has no conversion to canonical per-mille; value kept in place.",
                    DiagnosticSeverity.Error) });
        }

        if (reference.Equals("unspecified", StringComparison.OrdinalIgnoreCase))
        {
            diags.Add(new Diagnostic("Q.REF_UNSPECIFIED",
                "No isotope reference/scale provided.", DiagnosticSeverity.Warning));
        }

        return new Quantity(text, canonical, unit == "" ? "permil" : unit, reference, diags);
    }
}

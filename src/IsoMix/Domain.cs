using System.Text.Json;
using System.Text.Json.Nodes;

namespace IsoMix;

/// <summary>Stable reference to one version of one material, e.g. "endmember/E3@2".</summary>
public record MaterialRef(string Id, int Version)
{
    public override string ToString() => $"{Id}@{Version}";

    public static MaterialRef Parse(string s)
    {
        int at = s.LastIndexOf('@');
        if (at <= 0) throw new FormatException($"Bad material ref '{s}'");
        return new MaterialRef(s[..at], int.Parse(s[(at + 1)..]));
    }
}

public record MaterialRow(string Id, int Version, string Kind, int Ordinal, string BatchId,
    string RawJson, string? Supersedes, string CreatedUtc)
{
    public MaterialRef Ref => new(Id, Version);
    public JsonNode Raw => JsonNode.Parse(RawJson)!;
}

public record BranchRow(string Id, string Name, string? ParentId, string? BaseEventId,
    string? HeadEventId, string CreatedUtc);

public record EventRow(string Id, string BranchId, long Seq, string Kind,
    string PayloadJson, string DepsJson, string CreatedUtc)
{
    public object ToDto() => new
    {
        id = Id,
        branchId = BranchId,
        seq = Seq,
        kind = Kind,
        payload = JsonNode.Parse(PayloadJson),
        deps = JsonNode.Parse(DepsJson),
        createdUtc = CreatedUtc
    };
}

public record AnalysisRow(string Id, string EventId, string BranchId, string Status,
    string CandidatesJson, string DiagnosticsJson, string DepsJson, bool Stale,
    string? InvalidatedBy, string CreatedUtc)
{
    public object ToDto() => new
    {
        id = Id,
        eventId = EventId,
        branchId = BranchId,
        status = Status,
        candidates = JsonNode.Parse(CandidatesJson),
        diagnostics = JsonNode.Parse(DiagnosticsJson),
        deps = JsonNode.Parse(DepsJson),
        stale = Stale,
        invalidatedBy = InvalidatedBy,
        createdUtc = CreatedUtc
    };
}

public static class AnalysisStatus
{
    public const string Feasible = "feasible";
    public const string IdentifiabilityInsufficient = "identifiability_insufficient";
    public const string OutsideEnvelope = "outside_envelope";
    public const string InfeasiblePrior = "infeasible_prior";
    public const string Infeasible = "infeasible";
}

public static class RuleCodes
{
    public const string IdentifiabilityHadamard = "RULE_IDENTIFIABILITY_HADAMARD";
    public const string CovariancePropagation = "RULE_COVARIANCE_PROPAGATION";
    public const string SimplexExact = "RULE_SIMPLEX_EXACT_NO_RENORMALIZATION";
    public const string MissingIsotopeDimension = "DIAG_MISSING_ISOTOPE_DIMENSION";
    public const string UnitUnconverted = "DIAG_UNIT_UNCONVERTED";
    public const string MultipleFeasibleRegions = "DIAG_MULTIPLE_FEASIBLE_REGIONS";
    public const string SampleOutsideEnvelope = "DIAG_SAMPLE_OUTSIDE_ENVELOPE";
}

public class ConflictException : Exception
{
    public string BranchId { get; }
    public string? ExpectedHead { get; }
    public string? CurrentHead { get; }
    public JsonNode Detail { get; }

    public ConflictException(string branchId, string? expectedHead, string? currentHead, JsonNode detail)
        : base($"Branch '{branchId}' head conflict: expected '{expectedHead}', current '{currentHead}'.")
    {
        BranchId = branchId;
        ExpectedHead = expectedHead;
        CurrentHead = currentHead;
        Detail = detail;
    }
}

public static class J
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Str(JsonNode node) => node.ToJsonString(Options);
    public static JsonNode Parse(string s) => JsonNode.Parse(s)!;
}

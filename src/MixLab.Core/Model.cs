using System.Text.Json;
using System.Text.Json.Serialization;

namespace MixLab.Core;

public static class J
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public static string Ser<T>(T value) => JsonSerializer.Serialize(value, Opts);
    public static T De<T>(string json) => JsonSerializer.Deserialize<T>(json, Opts)!;
}

// ---- 来源层 DTO：所有记录保留原始表示（RawJson），派生层才做规范化 ----

public record IsotopeValue(string Isotope, double Value, string Unit, string? Basis);

public record SampleDto(string Id, string Name, List<IsotopeValue> Values, string RawJson);

public record EndmemberDist(string Isotope, double Mean, double Sd, string Unit, string? Basis);

public record EndmemberDto(string Id, string Name, string Version, List<EndmemberDist> Distributions, string RawJson);

public record CovarianceDto(string Id, string Name, List<string> Isotopes, List<List<double>> Matrix, List<string> Units, string RawJson);

public record FractionationEntry(string Isotope, double Delta, string Unit, string? Basis);

public record FractionationDto(string Id, string Version, List<FractionationEntry> Entries, string RawJson);

public record PriorDto(string Id, string Name, string Kind, JsonElement Params, string RawJson);

public record ImportBatchDto(
    string Id,
    string Label,
    List<SampleDto> Samples,
    List<EndmemberDto> Endmembers,
    List<CovarianceDto> Covariances,
    List<FractionationDto> Fractionations,
    List<PriorDto> Priors,
    string RawJson);

// ---- 事件与分支 ----

public record BranchRow(
    string Id, string Name, string? ParentId, string? BaseEventId,
    string? HeadEventId, long Version, string CreatedAt, bool Archived);

public record EventRow(
    string Id, string BranchId, long Seq, string Kind,
    string PayloadJson, string Actor, string CreatedAt);

public record AnalysisRunRow(
    string Id, string BranchId, string EventId, string SampleId,
    string DepsJson, string Status, string FlagsJson, string RegionsJson,
    string CandidatesJson, string ResidualsJson, string DiagnosticsJson,
    bool Stale, string? StaleReason, string CreatedAt);

public record InvalidationRow(
    string Id, string EventId, string ChangedDep, string AffectedRunIdsJson, string CreatedAt);

public record AdjudicationState(string CandidateId, string RunId, string Decision, string Rationale, string EventId, string Actor);

public record DepsInEffect(string? FractionationVersion, Dictionary<string, string> EndmemberVersions);

public record BranchView(
    BranchRow Branch,
    List<EventRow> Events,
    List<AnalysisRunRow> Runs,
    Dictionary<string, AdjudicationState> Adjudications,
    DepsInEffect Deps,
    long? UpToSeq);

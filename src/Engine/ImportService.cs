using System.Text.Json.Nodes;
using IsotopeMixing.Cases;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Engine;

public sealed class ImportDiagnostic
{
    public required string Code { get; set; }
    public required string Uid { get; set; }
    public required string Message { get; set; }
}

/// <summary>
/// Imports a case as raw records (original bytes preserved verbatim). Interpretation into
/// endmembers/samples/scenarios happens in a separate projection. A failure anywhere in
/// the batch rolls back the entire import - no half records.
/// </summary>
public sealed class ImportService
{
    private readonly Repository _repo;
    public ImportService(Repository repo) { _repo = repo; }

    public sealed record ImportedBatch(string BatchId, List<ImportDiagnostic> Diagnostics);

    public ImportedBatch ImportCase(CaseFile file, JsonNode manifestNode, string purpose, string? batchId = null)
    {
        var bid = batchId ?? DeterministicIds.Of("batch", $"{file.CaseId}|{file.GetHashCode()}|{purpose}");
        // Distinct batch per purpose but stable across reloads: incorporate a version field if present.
        bid = DeterministicIds.Of("batch", $"{file.CaseId}|{purpose}");
        _repo.CreateImportBatch(bid, purpose, manifestNode);
        var rows = new List<RawRecordRow>();
        var diagnostics = new List<ImportDiagnostic>();
        int ordinal = 0;

        void Add<T>(string kind, string uid, T entity, string? unit, string? datum, string parseStatus, List<ImportDiagnostic> diags)
        {
            var raw = JsonNode.Parse(Json.Ser(entity))!;
            var text = raw.ToJsonString();
            rows.Add(new RawRecordRow
            {
                Uid = uid, BatchId = bid, RecordKind = kind, Ordinal = ordinal++,
                RawJson = text, RawText = text, Unit = unit, Datum = datum,
                ByteSha256 = Repository.Sha256(text),
                ParseStatus = parseStatus,
                DiagnosticsJson = Json.Ser(diags.Select(d => new { code = d.Code, uid = d.Uid, message = d.Message }))
            });
        }

        Add("axis", "axes", file.Axes, null, "mixed", "parsed", new());
        Add("endmember", "endmembers", file.Endmembers, null, null, "parsed", new());
        Add("endmember_set", "endmember_sets", file.EndmemberSets, null, null, "parsed", new());
        Add("covariance", "covariances", file.Covariances, null, null, "parsed", new());
        Add("fractionation", "fractionations", file.Fractionations, null, null, "parsed", new());
        Add("prior", "priors", file.Priors, null, null, "parsed", new());

        foreach (var s in file.Samples)
        {
            var diags = new List<ImportDiagnostic>();
            foreach (var ax in file.Axes)
            {
                if (!s.Values.TryGetValue(ax.Key, out var val) || val is null)
                    diags.Add(new ImportDiagnostic { Code = "MISSING_ISOTOPE_DIMENSION", Uid = s.Uid, Message = $"sample {s.Uid} has no value for axis {ax.Key}; null retained" });
                else if (s.UnitsMode == "mixed" && s.UnitOverrides?.TryGetValue(ax.Key, out var u) == true && u != ax.Unit)
                {
                    var converted = UnitSystem.Convert(ax.Key, u, val.Value, out var diag);
                    if (diag is not null) diags.Add(new ImportDiagnostic { Code = "UNCONVERTIBLE_UNIT", Uid = s.Uid, Message = diag });
                }
            }
            string status = diags.Count == 0 ? "parsed" : diags.Any(d => d.Code == "UNCONVERTIBLE_UNIT") ? "partial" : "parsed";
            Add("sample", s.Uid, s, string.Join(",", file.Axes.Select(a => a.Unit)), "mixed", status, diags);
            diagnostics.AddRange(diags);
        }

        foreach (var sc in file.Scenarios)
            Add("scenario", sc.Uid, sc, null, null, "parsed", new());
        Add("replay", "replay", file.Replay, null, null, "parsed", new());

        _repo.CommitBatch(bid, rows);
        return new ImportedBatch(bid, diagnostics);
    }
}

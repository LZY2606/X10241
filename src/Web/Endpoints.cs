using System.Text.Json.Nodes;
using IsotopeMixing.Cases;
using IsotopeMixing.Engine;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Web;

public static class Endpoints
{
    public static void Map(WebApplication app, AppServices svc, string caseDir, string dbPath)
    {
        string BatchId() => $"batch_{svc.Repository.AllBatches().FirstOrDefault()?.Replace("batch_", "")}";

        string? CurrentBatch() => svc.Repository.AllBatches().FirstOrDefault();

        (CaseFile file, JsonNode node)? LoadCase()
        {
            var (file, node, _) = CaseLoader.LoadFromDirectory(caseDir);
            return (file, node);
        }

        double Confidence(CaseFile f) => double.TryParse(f.ConfidenceLevel, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 0.95;

        app.MapPost("/api/load-sample", () =>
        {
            if (CurrentBatch() is not null)
                return Results.Ok(new { alreadyLoaded = true, batchId = CurrentBatch() });
            var loaded = LoadCase()!.Value;
            var imported = svc.Importer.ImportCase(loaded.file, loaded.node, "initial-import");
            var report = svc.Replay.Run(loaded.file, imported.BatchId, Confidence(loaded.file));
            return Results.Ok(new
            {
                loaded = true,
                batchId = imported.BatchId,
                diagnostics = imported.Diagnostics,
                replayLog = report.Events,
                conflicts = report.Conflicts
            });
        });

        app.MapGet("/api/state", () =>
        {
            var batch = CurrentBatch();
            var branches = svc.Branches.ListBranches().Select(b => new
            {
                branchId = b.BranchId, parentBranchId = b.ParentBranchId, name = b.Name,
                createdAt = b.CreatedAt, note = b.Note, baselineBatchId = b.BaselineBatchId,
                headSeq = svc.Branches.HeadSeq(b.BranchId)
            }).ToList<object>();

            var raws = batch is null ? new List<object>() : svc.Repository.ReadRawByBatch(batch).Select(r => new
            {
                uid = r.uid, kind = r.kind, raw = JsonNode.Parse(r.raw), parseStatus = r.parseStatus,
                diagnostics = JsonNode.Parse(r.diagnostics),
                byteSha256 = svc.Repository.AllRawRecords().First(x => x.Uid == r.uid && x.BatchId == batch).ByteSha256
            }).ToList<object>();

            var results = svc.Branches.ListBranches().SelectMany(b => svc.Results.ForBranch(b.BranchId))
                .Select(r => ResultJson(r, svc)).ToList<object>();

            var events = svc.Branches.ListBranches().SelectMany(b => svc.Branches.Events(b.BranchId)
                .Select(e => (object)new
                {
                    branchId = b.BranchId, seq = e.Seq, eventId = e.EventId, at = e.At,
                    type = e.EventType, payload = e.Payload,
                    producedResultId = e.ProducedResultId, undoOfEventId = e.UndoOfEventId
                })).OrderBy(e => ((dynamic)e).at).ToList();

            var decisions = svc.Branches.ListBranches().SelectMany(b => svc.Decisions.ForBranch(b.BranchId))
                .Select(d => (object)new
                {
                    d.DecisionId, d.BranchId, d.EventId, d.Seq, d.ScenarioId, d.ResultId,
                    d.Verdict, d.Rationale, d.At, d.Revoked, d.RevokeEventId
                }).ToList();

            var allDiag = batch is null ? new List<object>() : svc.Repository.AllRawRecords()
                .Where(r => r.BatchId == batch && r.ParseStatus != "parsed")
                .Select(r => (object)new { r.Uid, r.ParseStatus, r.Unit, diagnostics = JsonNode.Parse(r.DiagnosticsJson) })
                .ToList();

            double conf = LoadCase() is { } lc ? Confidence(lc.file) : 0.95;
            return Results.Ok(new
            {
                dbPath, schemaVersion = svc.Db is null ? 0 : SchemaVersionSafe(svc),
                branches, rawRecords = raws, results, events, decisions,
                diagnostics = allDiag, confidenceLevel = conf,
                replay = new
                {
                    ran = events.Any(e => ((dynamic)e).type == "counterexample_started"),
                    conflicts = events.Where(e => ((dynamic)e).type == "decision_made").ToList().Count
                }
            });
        });

        app.MapPost("/api/branches", (BranchCreateReq req) =>
        {
            string id = req.BranchId ?? DeterministicIds.Of("br", $"{req.ParentBranchId}|{req.Name}|{Guid.NewGuid()}");
            svc.Branches.CreateBranch(id, req.ParentBranchId, req.Name, req.Note ?? "", CurrentBatch());
            return Results.Ok(new { branchId = id });
        });

        app.MapPost("/api/evaluate", (EvaluateReq req) =>
        {
            var batch = CurrentBatch() ?? throw new InvalidOperationException("no batch loaded");
            var comp = svc.Engine.EvaluateScenario(req.BranchId, batch, req.ScenarioId,
                req.Confidence ?? 0.95, req.ExpectedSeq);
            return Results.Ok(new { eventId = comp.EventId, seq = comp.Seq, resultIds = comp.ResultIds });
        });

        app.MapPost("/api/decide", (DecideReq req) =>
        {
            try
            {
                var (decisionId, eventId, seq) = svc.DecisionService.Decide(
                    req.BranchId, req.ScenarioId, req.ResultId, req.Verdict, req.Rationale ?? "", req.ExpectedSeq);
                return Results.Ok(new { decisionId, eventId, seq });
            }
            catch (ConflictException ex)
            {
                var mine = new { req.BranchId, req.ScenarioId, req.Verdict, expectedSeq = req.ExpectedSeq };
                var theirs = svc.Branches.Events(req.BranchId).Last(e => e.EventType == "decision_made");
                return Results.Conflict(new
                {
                    error = "VERSION_CONFLICT",
                    message = ex.Message,
                    expectedNextSeq = ex.ExpectedNextSeq,
                    actualNextSeq = ex.ActualNextSeq,
                    mine,
                    theirs = new { seq = theirs.Seq, eventId = theirs.EventId, payload = theirs.Payload }
                });
            }
        });

        app.MapPost("/api/revoke", (RevokeReq req) =>
        {
            var eid = svc.DecisionService.Revoke(req.BranchId, req.DecisionId, req.ExpectedSeq);
            return Results.Ok(new { eventId = eid });
        });

        app.MapPost("/api/revise", (ReviseReq req) =>
        {
            var payload = JsonNode.Parse(Json.Ser((object?)req.Payload ?? new Dictionary<string,object>())) ?? new JsonObject();
            var (eid, seq, affected, scenarios) = svc.Engine.RecordRevision(
                req.BranchId, req.Kind == "source" ? "source" : "parameter",
                req.Target, payload, req.ExpectedSeq);
            return Results.Ok(new { eventId = eid, seq, invalidatedResults = affected, affectedScenarios = scenarios });
        });

        app.MapGet("/api/provenance/{resultId}", (string resultId) =>
        {
            var r = svc.Results.Get(resultId);
            if (r is null) return Results.NotFound(new { error = "result not found" });
            return Results.Ok(new
            {
                result = ResultJson(r, svc),
                dependencies = svc.Results.Dependencies(resultId),
                createdEvent = svc.Branches.Events(r.BranchId).First(e => e.EventId == r.CreatedEventId)
            });
        });

        app.MapGet("/api/branch/{branchId}/at/{seq}", (string branchId, long seq) =>
        {
            // Time-travel view: reconstruct conclusions visible at exactly this event.
            var evs = svc.Branches.Events(branchId, seq);
            var results = svc.Results.ForBranch(branchId)
                .Where(r => r.CreatedSeq <= seq && (r.InvalidatedEventId is null ||
                    !evs.Any(e => e.EventId == r.InvalidatedEventId)))
                .Select(r => ResultJson(r, svc)).ToList();
            return Results.Ok(new { branchId, seq, events = evs.Select(e => new { e.Seq, e.EventId, e.EventType, e.Payload }), results });
        });

        app.MapPost("/api/replay", () =>
        {
            var batch = CurrentBatch() ?? throw new InvalidOperationException("load sample first");
            var lc = LoadCase()!.Value;
            var report = svc.Replay.Run(lc.file, batch, Confidence(lc.file));
            return Results.Ok(new { report.MainBranch, report.CounterBranches, log = report.Events, conflicts = report.Conflicts, focusedBranch = report.FocusedBranch });
        });

        app.MapGet("/api/export", () =>
        {
            var bytes = DatabasePortable.Export(svc.Db);
            return Results.File(bytes, "application/octet-stream", "isotope-archive.sqlite");
        });

        app.MapPost("/api/import-archive", async (HttpRequest request) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms);
            var count = DatabasePortable.Replace(dbPath, ms.ToArray());
            return Results.Ok(new { replaced = true, importedBatches = count });
        });
    }

    private static int SchemaVersionSafe(AppServices svc)
    {
        using var cn = svc.Db.Open();
        return Db.Schema.GetVersion(cn);
    }

    public static object ResultJson(ResultRow r, AppServices svc) => new
    {
        resultId = r.ResultId, branchId = r.BranchId, createdEventId = r.CreatedEventId,
        createdSeq = r.CreatedSeq, scenarioId = r.ScenarioId, scenarioLabel = r.ScenarioLabel,
        sampleUid = r.SampleUid, endmemberSetId = r.EndmemberSetId, covUid = r.CovUid,
        fracVersionId = r.FracVersionId, priorId = r.PriorId, unit = r.Unit, datum = r.Datum,
        status = r.Status, identifiability = r.Identifiability, feasible = r.Feasible,
        dof = r.Dof, chi2 = r.Chi2, pValue = r.PValue,
        bestWeights = r.BestWeightsJson is null ? null : JsonNode.Parse(r.BestWeightsJson),
        residuals = r.ResidualJson is null ? null : JsonNode.Parse(r.ResidualJson),
        region = r.RegionJson is null ? null : JsonNode.Parse(r.RegionJson),
        assessments = JsonNode.Parse(r.AssessmentsJson),
        inputs = JsonNode.Parse(r.InputsJson),
        invalidated = r.Invalidated, invalidatedEventId = r.InvalidatedEventId, invalidationScope = r.InvalidationScope
    };
}

public sealed class BranchCreateReq
{
    public string? BranchId { get; set; }
    public string? ParentBranchId { get; set; }
    public string Name { get; set; } = "";
    public string? Note { get; set; }
}
public sealed class EvaluateReq
{
    public string BranchId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public double? Confidence { get; set; }
    public long? ExpectedSeq { get; set; }
}
public sealed class DecideReq
{
    public string BranchId { get; set; } = "";
    public string ScenarioId { get; set; } = "";
    public string? ResultId { get; set; }
    public string Verdict { get; set; } = "";
    public string? Rationale { get; set; }
    public long? ExpectedSeq { get; set; }
}
public sealed class RevokeReq
{
    public string BranchId { get; set; } = "";
    public string DecisionId { get; set; } = "";
    public long? ExpectedSeq { get; set; }
}
public sealed class ReviseReq
{
    public string BranchId { get; set; } = "";
    public string Kind { get; set; } = "parameter";
    public string Target { get; set; } = "";
    public Dictionary<string, object>? Payload { get; set; }
    public long? ExpectedSeq { get; set; }
}

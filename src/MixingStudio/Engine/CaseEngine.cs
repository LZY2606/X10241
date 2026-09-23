using System.Text.Json;
using MixingStudio.Domain;
using MixingStudio.Math;
using MixingStudio.Store;

namespace MixingStudio.Engine;

public sealed record MaterialInput(string Id, string Kind, string RawJson);

public sealed class ConflictException : Exception
{
    public long ExpectedSeq { get; }
    public long ActualSeq { get; }
    public ConflictException(long expected, long actual)
        : base($"branch head conflict: expected seq {expected}, actual {actual}")
    { ExpectedSeq = expected; ActualSeq = actual; }
}

/// <summary>
/// Orchestrates provenance-preserving operations. Every mutation is an event;
/// source records are never rewritten; invalidation is computed from dependencies.
/// </summary>
public sealed class CaseEngine
{
    private readonly Repository _repo;

    public CaseEngine(Repository repo) => _repo = repo;

    public record MaterialRef(string Id, string Kind, int Version, string Sha256);

    // ---------- import (transactional: all-or-nothing) ----------

    public (string eventId, List<MaterialRef> refs) ImportMaterials(string branchId, List<MaterialInput> inputs, string note)
    {
        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        try
        {
            long seq = NextSeqInTx(conn);
            string eventId = $"evt-{seq:D4}";
            var refs = new List<MaterialRef>();
            foreach (var input in inputs)
            {
                // validate JSON before persisting; a single bad record aborts the whole import
                using var doc = JsonDocument.Parse(input.RawJson);
                int version = NextMaterialVersionInTx(conn, input.Id);
                var rec = new MaterialRecord(input.Id, input.Kind, version, input.RawJson, Repository.Sha256(input.RawJson), eventId);
                _repo.InsertMaterial(conn, tx, rec);
                refs.Add(new MaterialRef(rec.Id, rec.Kind, rec.Version, rec.Sha256));
            }
            InsertEventInTx(conn, tx, new EventRecord(eventId, branchId, seq, "MaterialsImported",
                Json.To(new { note, materials = refs.Select(r => new { r.Id, r.Kind, r.Version, r.Sha256 }) }),
                Json.To(refs), Now()));
            BumpBranchInTx(conn, tx, branchId, seq);
            tx.Commit();
            return (eventId, refs);
        }
        catch
        {
            tx.Rollback(); // failed import leaves no half-written records
            throw;
        }
    }

    // ---------- derivation ----------

    public string Derive(string branchId, List<MaterialRef> refs, double chi2Scale = 1.0, string? note = null)
    {
        var sample = Json.From<SampleData>(GetRef(refs, MaterialKinds.Sample).RawJson);
        var endmembers = Json.From<EndmemberSetData>(GetRef(refs, MaterialKinds.Endmembers).RawJson);
        var fractionation = Json.From<FractionationData>(GetRef(refs, MaterialKinds.Fractionation).RawJson);
        var prior = Json.From<PriorData>(GetRef(refs, MaterialKinds.Prior).RawJson);

        var result = MixingModel.Derive(sample, endmembers, fractionation, prior, chi2Scale);

        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        long seq = NextSeqInTx(conn);
        string eventId = $"evt-{seq:D4}";
        InsertEventInTx(conn, tx, new EventRecord(eventId, branchId, seq, "DerivationCompleted",
            Json.To(new { note, chi2Scale, result = result.ToEventPayload() }),
            Json.To(refs), Now()));
        foreach (var region in result.Regions)
        {
            var cand = new CandidateRecord($"cand-{seq:D4}-r{region.Index}", eventId, branchId, region.Index,
                Json.To(new { region.Points, region.Representative, region.MinD2, region.Residual }),
                "proposed", null);
            _repo.InsertCandidate(conn, tx, cand);
        }
        BumpBranchInTx(conn, tx, branchId, seq);
        tx.Commit();
        return eventId;
    }

    // ---------- adjudication (optimistic concurrency on branch head) ----------

    public string Adjudicate(string branchId, long expectedHeadSeq, string candidateId, string decision, string rationale)
    {
        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        var branch = _repo.Branch(branchId) ?? throw new InvalidOperationException($"unknown branch {branchId}");
        if (branch.HeadSeq != expectedHeadSeq)
            throw new ConflictException(expectedHeadSeq, branch.HeadSeq);

        long seq = NextSeqInTx(conn);
        string eventId = $"evt-{seq:D4}";
        string newStatus = decision == "accept" ? "accepted" : "rejected";
        _repo.SetCandidateStatus(conn, tx, candidateId, newStatus, eventId);
        InsertEventInTx(conn, tx, new EventRecord(eventId, branchId, seq, "AdjudicationRecorded",
            Json.To(new { candidateId, decision, rationale }),
            Json.To(Array.Empty<MaterialRef>()), Now()));
        BumpBranchInTx(conn, tx, branchId, seq);
        tx.Commit();
        return eventId;
    }

    /// <summary>Undo is append-only: a new event restores the candidate's previous status.</summary>
    public string UndoAdjudication(string branchId, long expectedHeadSeq, string candidateId)
    {
        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        var branch = _repo.Branch(branchId) ?? throw new InvalidOperationException($"unknown branch {branchId}");
        if (branch.HeadSeq != expectedHeadSeq)
            throw new ConflictException(expectedHeadSeq, branch.HeadSeq);
        var cand = _repo.Candidates(branchId).FirstOrDefault(c => c.Id == candidateId)
            ?? throw new InvalidOperationException($"unknown candidate {candidateId}");

        long seq = NextSeqInTx(conn);
        string eventId = $"evt-{seq:D4}";
        _repo.SetCandidateStatus(conn, tx, candidateId, "proposed", eventId);
        InsertEventInTx(conn, tx, new EventRecord(eventId, branchId, seq, "AdjudicationUndone",
            Json.To(new { candidateId, restoredStatus = "proposed", previousAdjudicationEvent = cand.AdjudicationEventId }),
            Json.To(Array.Empty<MaterialRef>()), Now()));
        BumpBranchInTx(conn, tx, branchId, seq);
        tx.Commit();
        return eventId;
    }

    // ---------- branching (reinterpretation never mutates the old branch) ----------

    public string CreateBranch(string name, string parentBranchId, string baseEventId)
    {
        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        long seq = NextSeqInTx(conn);
        string branchId = $"branch-{name.ToLowerInvariant().Replace(' ', '-')}-{seq:D4}";
        _repo.InsertBranch(conn, tx, new BranchRecord(branchId, name, parentBranchId, baseEventId, seq));
        InsertEventInTx(conn, tx, new EventRecord($"evt-{seq:D4}", branchId, seq, "BranchCreated",
            Json.To(new { name, parentBranchId, baseEventId }),
            Json.To(Array.Empty<MaterialRef>()), Now()));
        tx.Commit();
        return branchId;
    }

    // ---------- dependency-based invalidation ----------

    /// <summary>
    /// A new version of a material invalidates exactly the derived events that
    /// reference older versions of that same material id — nothing else.
    /// Old conclusions stay readable; they are marked stale, never overwritten.
    /// </summary>
    public List<string> ComputeInvalidations(string newMaterialId, int newVersion, string triggeringEventId)
    {
        var affected = new List<string>();
        foreach (var ev in _repo.Events())
        {
            if (ev.Type != "DerivationCompleted") continue;
            var refs = Json.From<List<MaterialRef>>(ev.MaterialRefsJson);
            if (refs.Any(r => r.Id == newMaterialId && r.Version < newVersion))
                affected.Add(ev.Id);
        }
        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        long seq = NextSeqInTx(conn);
        string eventId = $"evt-{seq:D4}";
        foreach (var target in affected)
            _repo.InsertInvalidation(conn, tx, new InvalidationRecord(
                $"inv-{seq:D4}-{target}", eventId, "event", target,
                $"material '{newMaterialId}' superseded by version {newVersion}"));
        var firstBranch = _repo.Events().FirstOrDefault(e => e.Id == triggeringEventId)?.BranchId
            ?? _repo.Branches().First().Id;
        InsertEventInTx(conn, tx, new EventRecord(eventId, firstBranch, seq, "DependenciesInvalidated",
            Json.To(new { materialId = newMaterialId, newVersion, affectedEvents = affected }),
            Json.To(Array.Empty<MaterialRef>()), Now()));
        tx.Commit();
        return affected;
    }

    // ---------- export / import archive (identity, order, provenance preserved) ----------

    public string ExportArchive()
    {
        var archive = new
        {
            format = "mixing-studio-archive/1",
            materials = _repo.Materials(),
            branches = _repo.Branches(),
            events = _repo.Events(),
            candidates = _repo.Candidates(),
            invalidations = _repo.Invalidations()
        };
        return JsonSerializer.Serialize(archive, new JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportArchive(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        using var conn = _repo.Connect();
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var m in root.GetProperty("materials").EnumerateArray())
                _repo.InsertMaterial(conn, tx, new MaterialRecord(
                    m.GetProperty("Id").GetString()!, m.GetProperty("Kind").GetString()!,
                    m.GetProperty("Version").GetInt32(), m.GetProperty("RawJson").GetString()!,
                    m.GetProperty("Sha256").GetString()!, m.GetProperty("EventId").GetString()!));
            foreach (var b in root.GetProperty("branches").EnumerateArray())
                _repo.InsertBranch(conn, tx, new BranchRecord(
                    b.GetProperty("Id").GetString()!, b.GetProperty("Name").GetString()!,
                    b.TryGetProperty("ParentBranchId", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    b.GetProperty("BaseEventId").GetString()!, b.GetProperty("HeadSeq").GetInt64()));
            foreach (var e in root.GetProperty("events").EnumerateArray())
                _repo.InsertEvent(conn, tx, new EventRecord(
                    e.GetProperty("Id").GetString()!, e.GetProperty("BranchId").GetString()!,
                    e.GetProperty("Seq").GetInt64(), e.GetProperty("Type").GetString()!,
                    e.GetProperty("PayloadJson").GetString()!, e.GetProperty("MaterialRefsJson").GetString()!,
                    e.GetProperty("CreatedAt").GetString()!));
            foreach (var c in root.GetProperty("candidates").EnumerateArray())
                _repo.InsertCandidate(conn, tx, new CandidateRecord(
                    c.GetProperty("Id").GetString()!, c.GetProperty("EventId").GetString()!,
                    c.GetProperty("BranchId").GetString()!, c.GetProperty("RegionIndex").GetInt32(),
                    c.GetProperty("PolygonJson").GetString()!, c.GetProperty("Status").GetString()!,
                    c.TryGetProperty("AdjudicationEventId", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null));
            foreach (var i in root.GetProperty("invalidations").EnumerateArray())
                _repo.InsertInvalidation(conn, tx, new InvalidationRecord(
                    i.GetProperty("Id").GetString()!, i.GetProperty("EventId").GetString()!,
                    i.GetProperty("TargetType").GetString()!, i.GetProperty("TargetId").GetString()!,
                    i.GetProperty("Reason").GetString()!));
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ---------- helpers ----------

    public MaterialRecord GetRef(List<MaterialRef> refs, string kind)
    {
        var r = refs.First(x => x.Kind == kind);
        return _repo.Material(r.Id, r.Version)!;
    }

    private long NextSeqInTx(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(seq),0)+1 FROM events";
        return (long)cmd.ExecuteScalar()!;
    }

    private int NextMaterialVersionInTx(SqliteConnection conn, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version),0)+1 FROM materials WHERE id=$i";
        cmd.Parameters.AddWithValue("$i", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void InsertEventInTx(SqliteConnection conn, SqliteTransaction tx, EventRecord e) =>
        _repo.InsertEvent(conn, tx, e);

    private void BumpBranchInTx(SqliteConnection conn, SqliteTransaction tx, string branchId, long seq)
    {
        if (_repo.Branch(branchId) is null)
            _repo.InsertBranch(conn, tx, new BranchRecord(branchId, branchId, null, "evt-0001", seq));
        else
            _repo.UpdateBranchHead(conn, tx, branchId, seq);
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("o");
}

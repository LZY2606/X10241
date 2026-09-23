using System.Text.Json;
using System.Text.Json.Serialization;
using IsotopeMix.Domain;

namespace IsotopeMix.Infrastructure;

public sealed class ImportException : Exception
{
    public ImportException(string message) : base(message) { }
}

public sealed class AnalysisRecord
{
    public required string AnalysisEventId { get; set; }
    public required long Seq { get; set; }
    public required string Title { get; set; }
    public required string SampleId { get; set; }
    public required IReadOnlyList<string> EndmemberIds { get; set; }
    public required string FractionationId { get; set; }
    public required string PriorId { get; set; }
    public required NormalizedInputs Inputs { get; set; }
    public required SolverResult Result { get; set; }
    public required string SolverVersion { get; set; }
    public string? Decision { get; set; }
    public string? DecisionEventId { get; set; }
    public string? DecisionNote { get; set; }
    public string? ReversedByEventId { get; set; }
    public bool Stale { get; set; }
    public string? StaleReason { get; set; }
    public long? StaleSinceSeq { get; set; }
}

public sealed class BranchState
{
    public string CaseId { get; set; } = "";
    public string? Title { get; set; }
    public List<DimensionSpec> Dimensions { get; } = new();
    public Dictionary<string, RawSample> Samples { get; } = new();
    public Dictionary<string, RawEndmember> Endmembers { get; } = new();
    public Dictionary<string, FractionationSet> Fractionations { get; } = new();
    public Dictionary<string, PriorAssumption> Priors { get; } = new();

    public List<AnalysisRecord> Analyses { get; } = new();
    public Dictionary<string, List<DecisionEntry>> DecisionLog { get; } = new();
    public string? DefaultFractionationId { get; set; }
    public string? DefaultPriorId { get; set; }
}

public sealed class ImportedBaseline
{
    public required string CaseId { get; init; }
    public required string MainBranchId { get; init; }
    public required EventRow Event { get; init; }
}

public sealed class CaseService
{
    private readonly Store _store;
    public CaseService(Store store) => _store = store;

    public IReadOnlyList<string> Cases => _store.ListCases();

    // ---------------- 导入（单事务，失败整体回滚，不留半份记录）----------------

    public ImportedBaseline Import(CasePackage package, string author = "importer")
    {
        Validate(package);
        var tx = _store.BeginTransaction();
        string caseId = package.CaseId;
        string branchId = Store.NewId("branch");
        try
        {
            foreach (var mat in package.Materials ?? Array.Empty<RawMaterialDto>())
            {
                byte[] bytes = Convert.FromBase64String(mat.ContentBase64);
                string sha = _store.PutBlob(bytes, mat.MediaType);
                _store.InsertRaw(caseId, "material", mat.StableId,
                    JsonSerializer.Serialize(mat, Store.JsonOptions), sha, null, tx);
            }
            foreach (var s in package.Samples)
                _store.InsertRaw(caseId, "sample", s.StableId,
                    JsonSerializer.Serialize(s, Store.JsonOptions), null, null, tx);
            foreach (var e in package.Endmembers)
                _store.InsertRaw(caseId, "endmember", e.StableId,
                    JsonSerializer.Serialize(e, Store.JsonOptions), null, null, tx);
            foreach (var f in package.Fractionations)
                _store.InsertRaw(caseId, "fractionation", f.StableId,
                    JsonSerializer.Serialize(f, Store.JsonOptions), null, null, tx);
            foreach (var pr in package.Priors)
                _store.InsertRaw(caseId, "prior", pr.StableId,
                    JsonSerializer.Serialize(pr, Store.JsonOptions), null, null, tx);

            using (var cmd = _store.Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO branches(branch_id, case_id, name, parent_branch_id, head_seq)
                    VALUES($b,$c,'main',NULL,0)";
                cmd.Parameters.AddWithValue("$b", branchId);
                cmd.Parameters.AddWithValue("$c", caseId);
                cmd.ExecuteNonQuery();
            }

            var payload = new
            {
                caseId,
                title = package.Title,
                description = package.Description,
                schemaVersion = package.SchemaVersion,
                dimensions = package.Dimensions,
                defaultFractionationId = package.Fractionations[0].StableId,
                defaultPriorId = package.Priors[0].StableId,
                retainedRawKinds = new[] { "sample", "endmember", "fractionation", "prior", "covariance", "units", "assumption", "material" }
            };
            var ev = _store.AppendEvent(caseId, branchId, EventTypes.CaseImported, payload,
                expectedHeadSeq: 0, author: author, externalTx: tx);
            tx.Commit();
            return new ImportedBaseline { CaseId = caseId, MainBranchId = branchId, Event = ev };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        finally { tx.Dispose(); }
    }

    private static void Validate(CasePackage p)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(p.CaseId)) errors.Add("caseId 缺失");
        if (p.Dimensions.Count == 0) errors.Add("至少需要一个同位素维定义");
        if (p.Samples.Count == 0) errors.Add("至少需要一个样品");
        if (p.Endmembers.Count is < 2 or > 4) errors.Add("网格求解器支持 2-4 个端元");
        if (p.Fractionations.Count == 0) errors.Add("至少需要一个分馏版本");
        if (p.Priors.Count == 0) errors.Add("至少需要一个先验");
        foreach (var c in p.Samples.Select(x => x.StableId)
            .Concat(p.Endmembers.Select(x => x.StableId))
            .Concat(p.Fractionations.Select(x => x.StableId))
            .Concat(p.Priors.Select(x => x.StableId)))
            if (string.IsNullOrWhiteSpace(c)) errors.Add("存在空的稳定身份");
        foreach (var s in p.Samples)
            foreach (var dim in p.Dimensions)
                if (!s.Values.ContainsKey(dim.Code))
                    errors.Add($"样品 {s.StableId} 缺少同位素维 {dim.Code}（缺失值必须以显式原始表示给出）");
        if (p.Samples.Select(s => s.StableId).Distinct().Count() != p.Samples.Count
            || p.Endmembers.Select(e => e.StableId).Distinct().Count() != p.Endmembers.Count)
            errors.Add("稳定身份重复");
        if (errors.Count > 0)
            throw new ImportException("IMPORT_VALIDATION_FAILED: " + string.Join("; ", errors));
    }

    // ---------------- 状态归约 ----------------

    public BranchState Reduce(string branchId, long? upToSeq = null)
    {
        var events = _store.EventsOnBranch(branchId, upToSeq);
        if (events.Count == 0) throw new InvalidOperationException("空分支");
        var state = new BranchState();
        var caseId = events[0].CaseId;
        state.CaseId = caseId;
        var raws = _store.GetRaws(caseId);

        foreach (var ev in events)
        {
            using var doc = JsonDocument.Parse(ev.PayloadJson);
            var root = doc.RootElement;
            switch (ev.Type)
            {
                case EventTypes.CaseImported:
                    state.Title = root.GetProperty("title").GetString();
                    state.DefaultFractionationId = root.GetProperty("defaultFractionationId").GetString();
                    state.DefaultPriorId = root.GetProperty("defaultPriorId").GetString();
                    foreach (var dim in root.GetProperty("dimensions").EnumerateArray())
                        state.Dimensions.Add(new DimensionSpec(
                            dim.GetProperty("code").GetString()!,
                            dim.GetProperty("label").GetString()!,
                            dim.GetProperty("unit").GetString()!,
                            dim.TryGetProperty("coordinateDatum", out var datum) && datum.ValueKind == JsonValueKind.String
                                ? datum.GetString() : null));
                    LoadBaseline(state, raws);
                    break;

                case EventTypes.SampleRevised:
                    ApplySampleRevision(state, root);
                    InvalidateAnalyses(state, ev.Seq, $"源数据（样品 {root.GetProperty("stableId").GetString()}）被修订",
                        a => a.SampleId == root.GetProperty("stableId").GetString());
                    break;

                case EventTypes.FractionationRevised:
                    ApplyFractionationRevision(state, root);
                    string fid = root.GetProperty("stableId").GetString()!;
                    InvalidateAnalyses(state, ev.Seq, $"参考版本（分馏 {fid}）被修订",
                        a => a.FractionationId == fid);
                    break;

                case EventTypes.AnalysisComputed:
                    state.Analyses.Add(DeserializeAnalysis(ev, root, state));
                    break;

                case EventTypes.JudgmentRecorded:
                {
                    var entry = new DecisionEntry(
                        root.GetProperty("analysisEventId").GetString()!,
                        ev.EventId,
                        root.GetProperty("decision").GetString()!,
                        root.TryGetProperty("note", out var nn) && nn.ValueKind == JsonValueKind.String ? nn.GetString() : null);
                    state.DecisionLog.TryAdd(entry.AnalysisEventId, new List<DecisionEntry>());
                    state.DecisionLog[entry.AnalysisEventId].Add(entry);
                    break;
                }

                case EventTypes.DecisionReversed:
                {
                    string decisionEventId = root.GetProperty("decisionEventId").GetString()!;
                    foreach (var kv in state.DecisionLog)
                    {
                        var hit = kv.Value.FirstOrDefault(e => e.DecisionEventId == decisionEventId);
                        if (hit is not null) { kv.Value.Remove(hit); break; }
                    }
                    break;
                }
            }
        }
        FinalizeDecisions(state);
        return state;
    }

    private static void FinalizeDecisions(BranchState state)
    {
        foreach (var a in state.Analyses)
        {
            if (state.DecisionLog.TryGetValue(a.AnalysisEventId, out var log) && log.Count > 0)
            {
                var latest = log[^1];
                a.Decision = latest.Decision;
                a.DecisionEventId = latest.DecisionEventId;
                a.DecisionNote = latest.Note;
                a.ReversedByEventId = null;
            }
            else
            {
                a.Decision = null;
                a.DecisionEventId = null;
                a.DecisionNote = null;
            }
        }
    }

    public sealed record DecisionEntry(string AnalysisEventId, string DecisionEventId,
        string Decision, string? Note);

    private static void LoadBaseline(BranchState state, List<RawRow> raws)
    {
        foreach (var r in raws)
        {
            using var doc = JsonDocument.Parse(r.OriginalJson);
            var root = doc.RootElement;
            switch (r.Kind)
            {
                case "sample":
                {
                    var sr = ParseSample(root, r.OriginalJson);
                    state.Samples[r.StableId] = WithParsedQuantitiesStatic(sr, state.Dimensions);
                    break;
                }
                case "endmember":
                {
                    var er = ParseEndmember(root);
                    state.Endmembers[r.StableId] = WithParsedQuantitiesStatic(er, state.Dimensions);
                    break;
                }
                case "fractionation": state.Fractionations[r.StableId] = ParseFractionation(root); break;
                case "prior": state.Priors[r.StableId] = ParsePrior(root); break;
            }
        }
    }

    private static RawSample ParseSample(JsonElement root, string rawText) => new()
    {
        StableId = root.GetProperty("stableId").GetString()!,
        Label = root.GetProperty("label").GetString()!,
        RawText = root.TryGetProperty("rawText", out var rt) ? rt.GetString() : rawText,
        Covariance = root.TryGetProperty("covariance", out var cov) && cov.ValueKind == JsonValueKind.Object
            ? ParseCovariance(cov) : null,
        Values = root.GetProperty("values").EnumerateObject().ToDictionary(
            p => p.Name,
            p => new Quantity(p.Value.GetString()!, null, "", null))
    };

    private static CovarianceBlock? ParseCovariance(JsonElement cov) => new()
    {
        Dimensions = cov.GetProperty("dimensions").EnumerateArray().Select(x => x.GetString()!).ToList(),
        Matrix = cov.GetProperty("matrix").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToArray(),
        Unit = cov.TryGetProperty("unit", out var u) ? u.GetString() : null,
        RawText = cov.TryGetProperty("rawText", out var rt) ? rt.GetString() : null
    };

    private static RawEndmember ParseEndmember(JsonElement root) => new()
    {
        StableId = root.GetProperty("stableId").GetString()!,
        Label = root.GetProperty("label").GetString()!,
        RawText = root.TryGetProperty("rawText", out var rt) ? rt.GetString() : null,
        Modes = root.GetProperty("modes").EnumerateArray().Select(m => new EndmemberMode
        {
            ModeId = m.GetProperty("modeId").GetString()!,
            Label = m.GetProperty("label").GetString()!,
            Means = m.GetProperty("means").EnumerateObject().ToDictionary(
                p => p.Name, p => new Quantity(p.Value.GetString()!, null, "", null)),
            Covariance = m.TryGetProperty("covariance", out var cov) && cov.ValueKind == JsonValueKind.Object
                ? ParseCovariance(cov) : null
        }).ToList()
    };

    private static FractionationSet ParseFractionation(JsonElement root) => new()
    {
        StableId = root.GetProperty("stableId").GetString()!,
        Label = root.GetProperty("label").GetString()!,
        Version = root.GetProperty("version").GetString()!,
        Basis = root.TryGetProperty("basis", out var b) ? b.GetString() : null,
        RawText = root.TryGetProperty("rawText", out var rt) ? rt.GetString() : null,
        Offsets = root.GetProperty("offsets").EnumerateObject().ToDictionary(
            p => p.Name,
            p => (IReadOnlyDictionary<string, double>)p.Value.EnumerateObject()
                .ToDictionary(q => q.Name, q => q.Value.GetDouble()))
    };

    private static PriorAssumption ParsePrior(JsonElement root) => new()
    {
        StableId = root.GetProperty("stableId").GetString()!,
        Label = root.GetProperty("label").GetString()!,
        Basis = root.TryGetProperty("basis", out var b) ? b.GetString() : null,
        RawText = root.TryGetProperty("rawText", out var rt) ? rt.GetString() : null,
        Bounds = root.GetProperty("bounds").EnumerateObject().ToDictionary(
            p => p.Name,
            p =>
            {
                var pair = p.Value;
                double? lo = pair[0].ValueKind == JsonValueKind.Null ? null : pair[0].GetDouble();
                double? hi = pair[1].ValueKind == JsonValueKind.Null ? null : pair[1].GetDouble();
                return (lo, hi);
            })
    };

    // ---------- 把来源层原始表示转为带单位 Quantity（派生层）----------


    private static RawSample WithParsedQuantitiesStatic(RawSample raw, IReadOnlyList<DimensionSpec> dims)
    {
        var values = new Dictionary<string, Quantity>();
        foreach (var dim in dims)
        {
            string text = raw.Values.TryGetValue(dim.Code, out var q) ? q.Raw : "";
            values[dim.Code] = Normalizer.ParseQuantity(text, dim.Unit, dim.CoordinateDatum);
        }
        return raw with { Values = values };
    }

    private static RawEndmember WithParsedQuantitiesStatic(RawEndmember raw, IReadOnlyList<DimensionSpec> dims)
    {
        var modes = raw.Modes.Select(mode =>
        {
            var means = new Dictionary<string, Quantity>();
            foreach (var dim in dims)
            {
                string text = mode.Means.TryGetValue(dim.Code, out var q) ? q.Raw : "";
                means[dim.Code] = Normalizer.ParseQuantity(text, dim.Unit, dim.CoordinateDatum);
            }
            return mode with { Means = means };
        }).ToList();
        return raw with { Modes = modes };
    }

    // ---------- 修订（追加原始记录 + 事件；旧记录继续可读）----------

    public EventRow ReviseSample(string caseId, string branchId, RawSampleDto patch,
        long expectedHeadSeq, string author = "reviewer")
    {
        var branch = MustGetBranch(caseId, branchId);
        var tx = _store.BeginTransaction();
        try
        {
            _store.InsertRaw(caseId, "sample", patch.StableId,
                JsonSerializer.Serialize(patch, Store.JsonOptions), null, branchId, tx);
            var ev = _store.AppendEvent(caseId, branchId, EventTypes.SampleRevised,
                new { stableId = patch.StableId, raw = patch, revisionScope = "dependency-local" },
                expectedHeadSeq, author: author, externalTx: tx);
            tx.Commit();
            return ev;
        }
        catch { tx.Rollback(); throw; }
        finally { tx.Dispose(); }
    }

    public EventRow ReviseFractionation(string caseId, string branchId, FractionationDto patch,
        long expectedHeadSeq, string author = "reviewer")
    {
        var branch = MustGetBranch(caseId, branchId);
        var tx = _store.BeginTransaction();
        try
        {
            _store.InsertRaw(caseId, "fractionation", patch.StableId,
                JsonSerializer.Serialize(patch, Store.JsonOptions), null, branchId, tx);
            var ev = _store.AppendEvent(caseId, branchId, EventTypes.FractionationRevised,
                new { stableId = patch.StableId, version = patch.Version, raw = patch, revisionScope = "dependency-local" },
                expectedHeadSeq, author: author, externalTx: tx);
            tx.Commit();
            return ev;
        }
        catch { tx.Rollback(); throw; }
        finally { tx.Dispose(); }
    }

    private void ApplySampleRevision(BranchState state, JsonElement root)
    {
        var dto = JsonSerializer.Deserialize<RawSampleDto>(root.GetProperty("raw"), JsonOptions)!;
        var raw = ParseSample(root.GetProperty("raw"), root.GetProperty("raw").GetRawText());
        state.Samples[dto.StableId] = WithParsedQuantitiesStatic(raw, state.Dimensions);
    }

    private void ApplyFractionationRevision(BranchState state, JsonElement root)
    {
        var parsed = ParseFractionation(root.GetProperty("raw"));
        state.Fractionations[parsed.StableId] = parsed;
    }

    private static void InvalidateAnalyses(BranchState state, long seq, string reason,
        Func<AnalysisRecord, bool> predicate)
    {
        foreach (var a in state.Analyses)
            if (!a.Stale && predicate(a))
            {
                a.Stale = true;
                a.StaleReason = reason;
                a.StaleSinceSeq = seq;
            }
    }

    // ---------- 分析 ----------

    public EventRow ComputeAnalysis(string caseId, string branchId, AnalysisRequest request,
        long expectedHeadSeq, string author = "reviewer")
    {
        var branch = MustGetBranch(caseId, branchId);
        var state = Reduce(branchId);
        if (!state.Samples.ContainsKey(request.SampleId))
            throw new ImportException($"未知样品 {request.SampleId}");
        foreach (var id in request.EndmemberIds)
            if (!state.Endmembers.ContainsKey(id)) throw new ImportException($"未知端元 {id}");
        if (!state.Fractionations.ContainsKey(request.FractionationId))
            throw new ImportException($"未知分馏版本 {request.FractionationId}");
        if (!state.Priors.ContainsKey(request.PriorId))
            throw new ImportException($"未知先验 {request.PriorId}");

        var sample = WithParsedQuantities(state.Samples[request.SampleId], state.Dimensions);
        var endmembers = request.EndmemberIds
            .Select(id => WithParsedQuantities(state.Endmembers[id], state.Dimensions)).ToList();
        var frac = state.Fractionations[request.FractionationId];
        var prior = state.Priors[request.PriorId];

        var inputs = Normalizer.BuildProblem(
            Store.NewId("problem"), sample, endmembers, frac, prior, state.Dimensions, request.Options);
        var result = MixingModelSolver.Solve(inputs.Problem);

        var payload = new
        {
            title = request.Title ?? $"分析 {request.SampleId}",
            request,
            inputs = new
            {
                missingDimensions = inputs.MissingDimensions,
                conversionDiagnostics = inputs.ConversionDiagnostics
            },
            result,
            solverVersion = MixingModelSolver.SolverVersion
        };
        return _store.AppendEvent(caseId, branchId, EventTypes.AnalysisComputed, payload,
            expectedHeadSeq,
            dependsOnEventId: null,
            author: author);
    }

    private AnalysisRecord DeserializeAnalysis(EventRow ev, JsonElement root, BranchState state)
    {
        var req = JsonSerializer.Deserialize<AnalysisRequest>(root.GetProperty("request"), JsonOptions)!;
        var result = JsonSerializer.Deserialize<SolverResult>(root.GetProperty("result"), JsonOptions)!;
        var inputsElement = root.GetProperty("inputs");
        var missing = inputsElement.GetProperty("missingDimensions").EnumerateArray()
            .Select(x => x.GetString()!).ToList();
        var conv = inputsElement.TryGetProperty("conversionDiagnostics", out var cd)
            ? cd.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
        var sample = state.Samples[req.SampleId];
        var endmembers = req.EndmemberIds.Select(id => state.Endmembers[id]).ToList();
        var frac = state.Fractionations[req.FractionationId];
        var prior = state.Priors[req.PriorId];
        var problem = Normalizer.BuildProblem("replay@" + ev.EventId,
            sample, endmembers, frac, prior, state.Dimensions, req.Options).Problem;
        var inputs = new NormalizedInputs { Problem = problem, MissingDimensions = missing, ConversionDiagnostics = conv };
        return new AnalysisRecord
        {
            AnalysisEventId = ev.EventId,
            Seq = ev.Seq,
            Title = root.GetProperty("title").GetString()!,
            SampleId = req.SampleId,
            EndmemberIds = req.EndmemberIds,
            FractionationId = req.FractionationId,
            PriorId = req.PriorId,
            Inputs = inputs,
            Result = result,
            SolverVersion = root.GetProperty("solverVersion").GetString()!
        };
    }

    // ---------- 裁定（接受/否定/参数调整建议），可撤销 ----------

    public EventRow RecordDecision(string caseId, string branchId, string analysisEventId,
        string decision, string note, long expectedHeadSeq, string author = "reviewer")
    {
        if (decision is not ("accept" or "reject" or "adjust"))
            throw new ImportException("decision 必须是 accept / reject / adjust");
        var state = Reduce(branchId);
        var analysis = state.Analyses.FirstOrDefault(a => a.AnalysisEventId == analysisEventId)
            ?? throw new ImportException("未知分析事件");
        var payload = new { analysisEventId, decision, note, previousDecision = analysis.Decision };
        return _store.AppendEvent(caseId, branchId, EventTypes.JudgmentRecorded, payload,
            expectedHeadSeq, basisEventId: analysisEventId, author: author);
    }

    public EventRow ReverseDecision(string caseId, string branchId, string decisionEventId,
        long expectedHeadSeq, string author = "reviewer")
    {
        var target = _store.GetEvent(decisionEventId)
            ?? throw new ImportException("未知裁定事件");
        if (target.Type != EventTypes.JudgmentRecorded)
            throw new ImportException("只能撤销人工裁定事件");
        return _store.AppendEvent(caseId, branchId, EventTypes.DecisionReversed,
            new { decisionEventId }, expectedHeadSeq,
            basisEventId: decisionEventId, author: author);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

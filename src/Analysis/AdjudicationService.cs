using System.Text.Json;
using IsotopeMixing.Core;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Analysis;

/// <summary>
/// 人工裁定全部建模为追加事件：接受/否定候选、参数调整、撤销，
/// 从不改写原始数据。撤销通过追加反向事件实现，可回到任一分支节点。
/// </summary>
public sealed class AdjudicationService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly Store _store;
    public AdjudicationService(Store store) => _store = store;

    public BranchInfo CreateBranch(string caseId, string name, string? parentBranchId,
        string? forkAtEventId, string actor, string reason)
    {
        var branchId = Deterministic.BranchId(caseId, name);
        if (_store.ListBranches().Any(b => b.BranchId == branchId))
            throw new InvalidOperationException($"分支已存在：{name}");

        BranchInfo? parent = parentBranchId is null ? null : _store.GetBranch(parentBranchId);
        string? forkEvent = forkAtEventId ?? parent?.HeadEventId;
        var info = new BranchInfo(branchId, name, parentBranchId, forkEvent,
            DateTimeOffset.UtcNow, reason, forkEvent,
            forkEvent is null ? 0 : _store.GetEvent(forkEvent)?.Seq ?? 0);
        _store.CreateBranch(info);

        var payload = JsonSerializer.Serialize(new
        {
            name, parentBranchId, forkedFromEvent = forkEvent, reason
        }, JsonOpts);
        _store.AppendEvent(branchId, EventType.BranchCreated, branchId, payload, actor,
            basisRule: "REINTERPRETATION_BRANCH");
        return info;
    }

    public sealed record DecisionRequest(
        string BranchId, string SampleId, string CandidateId,
        string Action, // accept | reject
        string ExpectedParentEventId,
        string? Note,
        string Actor,
        string? EndmemberSetId,
        string? FractionationId,
        string? PriorsId);

    public EventRecord Decide(DecisionRequest req)
    {
        var analysis = _store.FindAnalysis(req.BranchId, req.SampleId)
            ?? throw new InvalidOperationException("尚无该样品的分析结果");
        bool accept = req.Action == "accept";
        var type = accept ? EventType.CandidateAccepted : EventType.CandidateRejected;

        var payload = JsonSerializer.Serialize(new
        {
            sampleId = req.SampleId,
            candidateId = req.CandidateId,
            analysisId = analysis.AnalysisId,
            atEventSeq = analysis.AtEventSeq,
            endmemberSetId = req.EndmemberSetId ?? analysis.EndmemberSetId,
            fractionationId = req.FractionationId ?? analysis.FractionationVersion,
            priorsId = req.PriorsId ?? analysis.PriorsId,
            note = req.Note
        }, JsonOpts);

        return _store.AppendEvent(req.BranchId, type, req.CandidateId, payload,
            req.Actor, expectedParentId: req.ExpectedParentEventId, note: req.Note);
    }

    /// <summary>撤销最近一次裁定：追加 Undone 事件，原事件保留可读。</summary>
    public EventRecord UndoLastDecision(string branchId, string actor)
    {
        var last = _store.ReadEvents(branchId)
            .LastOrDefault(e => e.Type is EventType.CandidateAccepted
                or EventType.CandidateRejected
                or EventType.ParameterAdjusted
                or EventType.PriorOverridden);
        if (last is null) throw new InvalidOperationException("没有可撤销的裁定");
        var payload = JsonSerializer.Serialize(new { undoneEvent = last.EventId, last.Type }, JsonOpts);
        return _store.AppendEvent(branchId, EventType.AdjudicationUndone, last.EventId,
            payload, actor, basisRule: "APPEND_ONLY_UNDO");
    }

    /// <summary>参数调整（如切换分馏版本/先验版本）：只追加事件，旧事件不动。</summary>
    public EventRecord AdjustParameters(string branchId, string actor,
        string? fractionationId, string? endmemberSetId, string? priorsId,
        string expectedParentEventId, string reason)
    {
        var payload = JsonSerializer.Serialize(new
        {
            fractionationId, endmemberSetId, priorsId, reason
        }, JsonOpts);
        return _store.AppendEvent(branchId, EventType.ParameterAdjusted, "parameters",
            payload, actor, expectedParentId: expectedParentEventId, note: reason,
            basisRule: RuleCodes.FractionationSwitch);
    }

    /// <summary>
    /// 源数据修订：新材料以新内容地址保存（旧字节继续可读）。
    /// 失效范围仅包含依赖被改 material_id 的分析，其他分支/分析保持原状。
    /// </summary>
    public EventRecord AmendMaterial(string branchId, string actor,
        Store.ImportUnit unit, string replacesMaterialId, string sampleKey, string reason,
        string expectedParentEventId)
    {
        _store.CommitImport(new[] { unit }, branchId, actor, basisRule: "LOCAL_SOURCE_AMENDMENT");
        var payload = JsonSerializer.Serialize(new
        {
            kind = unit.Material.Kind.ToString(),
            newMaterialId = unit.Material.MaterialId,
            replacesMaterialId,
            sampleId = sampleKey,
            replacesSampleId = sampleKey,
            reason
        }, JsonOpts);
        var amendEv = _store.AppendEvent(branchId, EventType.MaterialAmended,
            unit.Material.MaterialId, payload, actor,
            expectedParentId: expectedParentEventId, note: reason);

        _store.MarkInvalidated(branchId,
            new HashSet<string> { replacesMaterialId, unit.Material.MaterialId },
            amendEv.EventId, actor);
        return amendEv;
    }

    /// <summary>分支视图在指定事件处的状态快照（用于“回到任意旧事件”）。</summary>
    public BranchView ViewAt(string branchId, string? eventId)
    {
        long? upTo = eventId is null ? null : _store.GetEvent(eventId)?.Seq;
        var events = _store.ReadEvents(branchId, upTo);
        var decisions = events.Where(e => e.Type is
            EventType.CandidateAccepted or EventType.CandidateRejected).ToList();
        var undone = new HashSet<string>(events
            .Where(e => e.Type == EventType.AdjudicationUndone)
            .Select(e => JsonDocument.Parse(e.PayloadJson)
                .RootElement.GetProperty("undoneEvent").GetString()));
        var active = decisions.Where(e => !undone.Contains(e.EventId)).ToList();
        return new BranchView(branchId, events, active, events.Count == 0 ? null : events[^1].EventId);
    }
}

public sealed record BranchView(
    string BranchId,
    IReadOnlyList<EventRecord> Events,
    IReadOnlyList<EventRecord> ActiveDecisions,
    string? HeadEventIdAt);

using System.Text.Json;
using IsotopeMixing.Core;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Analysis;

public sealed record BranchSelection(
    string EndmemberSetId,
    string FractionationId,
    string PriorsId,
    IReadOnlyDictionary<string, string> CovarianceBySample,
    IReadOnlyList<string> SampleIds);

/// <summary>
/// 分支视图状态由事件重放得到（事件溯源）。材料是不可变内容寻址对象，
/// 分支只引用其 material_id，因此“重新解释”新建分支时旧分支与原始字节都继续可读。
/// </summary>
public sealed class AnalysisService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly Store _store;
    private readonly Materializer _materializer = new();
    private readonly MixingSolver _solver = new();

    public AnalysisService(Store store) => _store = store;

    public BranchSelection ResolveSelection(string branchId)
    {
        string? endmembers = null, fractionation = null, priors = null;
        var cov = new Dictionary<string, string>();
        var samples = new List<string>();

        foreach (var ev in _store.ReadEvents(branchId))
        {
            using var doc = JsonDocument.Parse(ev.PayloadJson);
            var root = doc.RootElement;
            if (ev.Type is not (EventType.MaterialImported or EventType.MaterialAmended)) continue;

            string kind = root.GetProperty("kind").GetString()!;
            string mid = ev.Type == EventType.MaterialAmended
                ? root.GetProperty("newMaterialId").GetString()!
                : root.GetProperty("materialId").GetString()!;

            if (kind == nameof(MaterialKind.Sample))
            {
                string sampleId = ev.Type == EventType.MaterialAmended
                    ? root.GetProperty("sampleId").GetString()!
                    : ReadFirstText(mid, "sample_id");
                if (ev.Type == EventType.MaterialAmended)
                {
                    string oldId = root.GetProperty("replacesSampleId").GetString()!;
                    int idx = samples.IndexOf(oldId);
                    if (idx >= 0) samples[idx] = sampleId; else samples.Add(sampleId);
                }
                else if (!samples.Contains(sampleId)) samples.Add(sampleId);
            }
            else if (kind == nameof(MaterialKind.EndmemberSet)) endmembers = mid;
            else if (kind == nameof(MaterialKind.Fractionation)) fractionation = mid;
            else if (kind == nameof(MaterialKind.Priors)) priors = mid;
            else if (kind == nameof(MaterialKind.Covariance))
            {
                string sampleId = ev.Type == EventType.MaterialAmended
                    ? root.GetProperty("sampleId").GetString()!
                    : _store.GetMaterial(mid)!.Values[0].RecordKey;
                cov[sampleId] = mid;
            }
        }

        return new BranchSelection(
            endmembers ?? "", fractionation ?? "", priors ?? "", cov,
            samples.Distinct().ToList());
    }

    private string ReadFirstText(string materialId, string field)
    {
        var m = _store.GetMaterial(materialId);
        return m!.Values.First(v => v.Field == field).RawText;
    }

    private string GetSampleId(string materialId)
    {
        var m = _store.GetMaterial(materialId)!;
        return m.Values.First(v => v.Field == "sample_id").RawText;
    }

    private string GetCovSampleId(string materialId)
    {
        var m = _store.GetMaterial(materialId)!;
        return m.Values.Select(v => v.RecordKey).First();
    }

    public const int Dimensions = 2;

    /// <summary>按当前分支选择重算所有样品分析，返回（含失效标记的）结果。不覆盖历史分析。</summary>
    public List<StoredAnalysis> RecomputeBranch(string branchId, string actor,
        string? explicitFractionationId = null, string? explicitEndmemberId = null)
    {
        var sel = ResolveSelection(branchId);
        string endmemberId = explicitEndmemberId ?? sel.EndmemberSetId;
        string fractionationId = explicitFractionationId ?? sel.FractionationId;
        string priorsId = sel.PriorsId;
        long headSeq = _store.GetBranch(branchId).HeadSeq;

        var endmembers = endmemberId == "" ? null :
            _materializer.ToEndmembers(_store.GetMaterial(endmemberId)!, Dimensions);
        var fracMat = fractionationId == "" ? null :
            _store.GetMaterial(fractionationId);
        var fractionation = fracMat is null
            ? new FractionationModel("", "F0:none", new double[Dimensions],
                Enumerable.Repeat("permil", Dimensions).ToArray())
            : _materializer.ToFractionation(fracMat, Dimensions);
        var priorMat = priorsId == "" ? null : _store.GetMaterial(priorsId);
        var sourceOrder = endmembers?.Sources.Select(s => s.SourceId).ToList() ?? new List<string>();
        var priors = priorMat is null
            ? new PriorBoundsModel("", "P0:none",
                new double?[sourceOrder.Count], new double?[sourceOrder.Count])
            : _materializer.ToPriors(priorMat, sourceOrder.Count, sourceOrder);

        var allMats = _store.ListMaterials();
        string SampleMaterialId(string sampleId) =>
            allMats.Last(m => m.Kind == MaterialKind.Sample
                && m.Values.Any(v => v.Field == "sample_id" && v.RawText == sampleId)).MaterialId;

        var results = new List<StoredAnalysis>();
        foreach (string sampleMatId in sel.SampleIds.Select(SampleMaterialId).Distinct())
        {
            var sampleMat = _store.GetMaterial(sampleMatId)!;
            var sample = _materializer.ToSample(sampleMat, Dimensions);
            string covMatId = sel.CovarianceBySample.TryGetValue(sample.SampleId, out var c)
                ? c : "";
            var covariance = covMatId == ""
                ? new CovarianceModel("", sample.SampleId,
                    LinearAlgebra.Identity(Dimensions), false)
                : _materializer.ToCovariance(_store.GetMaterial(covMatId)!, Dimensions);

            var sources = (endmembers?.Sources ?? new List<EndmemberSourceModel>())
                .Select(s => new SourceInput(s.SourceId, s.Label,
                    (double?[])s.Means.Clone(), s.Units, s.Present)).ToList();

            var req = new MixingRequest(
                sample.SampleId, sample.Label,
                sample.Means, sample.Present, covariance.Matrix, sources,
                fractionation.Delta, fractionation.VersionId,
                priors.Lower, priors.Upper, "permil");

            MixingResult result = _solver.Solve(req);
            string resultJson = JsonSerializer.Serialize(new
            {
                result,
                sourceMaterials = new
                {
                    sample = sampleMatId,
                    endmembers = endmemberId,
                    fractionation = fractionationId,
                    priors = priorsId,
                    covariance = covMatId,
                    rawDocuments = new[] { sampleMatId, endmemberId, fractionationId, priorsId, covMatId }
                        .Where(x => x != "").Select(id =>
                            _store.ListDocuments().FirstOrDefault(d => d.MaterialId == id)?.DocumentId)
                }
            }, JsonOpts);

            var dep = new[] { sampleMatId, endmemberId, fractionationId, priorsId, covMatId }
                .Where(x => !string.IsNullOrEmpty(x)).ToArray();
            var stored = new StoredAnalysis(
                Deterministic.AnalysisId(branchId, headSeq, sample.SampleId),
                branchId, headSeq, sample.SampleId, endmemberId, fractionation.VersionId,
                priorsId, covMatId, resultJson, result.Status.ToString(), DateTimeOffset.UtcNow)
            { DependsOnMaterialIds = dep };
            _store.SaveAnalysis(stored);
            results.Add(stored);
        }

        _store.AppendEvent(branchId, EventType.AnalysisRecomputed, "branch",
            JsonSerializer.Serialize(new
            {
                endmemberSetId = endmemberId,
                fractionationId,
                priorsId,
                samples = results.Select(r => r.SampleId),
                statuses = results.ToDictionary(r => r.SampleId, r => r.StatusSummary)
            }, JsonOpts), actor, basisRule: RuleCodes.CovariancePropagation);
        return results;
    }
}

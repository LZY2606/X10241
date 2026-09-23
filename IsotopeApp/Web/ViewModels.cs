using System.Text.Json;
using IsotopeApp.Analysis;
using IsotopeApp.Domain;
using IsotopeApp.Storage;

namespace IsotopeApp.Web;

public sealed record CandidateView(
    string CandidateId, int RegionIndex, double[] Proportions, double[] Residuals,
    double Statistic, int[] Modes, double[][] TotalCovariance);

public sealed record RegionView(
    int Index, double[] Center, double[] Min, double[] Max, int Cells, double BestStatistic, int[] Modes);

public sealed record TrialView(
    string TrialId, string BranchId, string SampleId, string SampleName,
    IReadOnlyList<string> Tracers, IReadOnlyList<string> SourceIds, IReadOnlyList<string> SourceNames,
    double[] Observation, double[][] MeasurementCovariance,
    string FractionationId, string FractionationLabel, double[] FractionationShifts,
    bool Feasible, bool InsideHull, bool Identifiable, string ConditionNumber,
    bool PriorInfeasible, double Threshold, double BestStatistic,
    IReadOnlyList<RegionView> Regions, IReadOnlyList<CandidateView> Candidates,
    IReadOnlyList<string> Diagnostics, int SelectedCandidate,
    IReadOnlyList<(double[][] Means, double[][,] Covs, double[] Weights)> Sources,
    string ResultEventId, int TrialVersion, string Status,
    IReadOnlyList<DependencyRef> Dependencies, string? Decision, string? DecisionComment);

/// <summary>Builds page models from stored trials without recomputing history.</summary>
public sealed class ViewModelBuilder
{
    private readonly Store _store;
    public ViewModelBuilder(Store store) { _store = store; }

    public TrialView Build(string branchId, string trialId, int? upToSeq = null, int selectedCandidate = 0)
    {
        var t = _store.GetTrialAt(branchId, trialId, upToSeq) ?? throw new KeyNotFoundException(trialId);
        var res = t.Result.RootElement;
        var sampleRow = _store.ActiveSample(branchId, t.SampleId);
        using var sampleDoc = JsonDocument.Parse(sampleRow.Json);
        string sampleName = sampleDoc.RootElement.GetProperty("name").GetString()!;
        double[] obs = res.TryGetProperty("observation", out var obsEl)
            ? obsEl.Deserialize<double[]>(JsonOpts.Default)!
            : Array.Empty<double>();
        var tracers = res.GetProperty("tracers").Deserialize<List<string>>(JsonOpts.Default) ?? new();
        var srcIds = res.GetProperty("sources").Deserialize<List<string>>(JsonOpts.Default) ?? new();

        var sourceIds = new List<string>(); var sourceNames = new List<string>();
        var sources = new List<(double[][], double[][,], double[])>();
        foreach (var dep in t.SourceVersions)
        {
            var row = _store.ActiveSource(branchId, dep.ObjectId);
            using var d = JsonDocument.Parse(row.Json);
            sourceIds.Add(dep.ObjectId);
            sourceNames.Add(d.RootElement.GetProperty("name").GetString()!);
            var modes = d.RootElement.GetProperty("data").GetProperty("modes");
            var meansList = new List<double[]>(); var covList = new List<double[,]>(); var weights = new List<double>();
            foreach (var m in modes.GetProperty("modes").EnumerateArray())
            {
                meansList.Add(m.GetProperty("mean").Deserialize<double[]>(JsonOpts.Default)!);
                var rows = m.GetProperty("cov").Deserialize<double[][]>(JsonOpts.Default)!;
                covList.Add(AnalysisService.ToJaggedReverse(rows));
                weights.Add(m.GetProperty("weight").GetDouble());
            }
            sources.Add((meansList.ToArray(), covList.ToArray(), weights.ToArray()));
        }

        // sample covariance from active row
        double[][] mc;
        var covRow = _store.CovarianceForSample(branchId, t.SampleId);
        if (covRow is not null)
        {
            using var cd = JsonDocument.Parse(covRow.Value.Json);
            var full = JsonSerializer.Deserialize<double[][]>(cd.RootElement.GetProperty("matrix").GetString()!, JsonOpts.Default)!;
            var allTracers = JsonSerializer.Deserialize<List<string>>(cd.RootElement.GetProperty("tracers").GetString()!, JsonOpts.Default)!;
            var idx = tracers.Select(tr => allTracers.IndexOf(tr)).ToArray();
            mc = idx.Select(i => idx.Select(j => full[i][j]).ToArray()).ToArray();
        }
        else mc = Array.Empty<double[]>();

        var fracRow = _store.ActiveFractionation(branchId, t.FractionationId);
        using var fd = JsonDocument.Parse(fracRow.Json);
        string fracLabel = fd.RootElement.GetProperty("label").GetString()!;
        var shiftsRaw = JsonSerializer.Deserialize<Dictionary<string, double>>(
            fd.RootElement.GetProperty("shifts").GetString()!, JsonOpts.Default)!;
        var shifts = tracers.Select(tr => shiftsRaw.GetValueOrDefault(tr)).ToArray();

        var regions = res.GetProperty("regions").EnumerateArray().Select(r => new RegionView(
            r.GetProperty("index").GetInt32(),
            r.GetProperty("center").Deserialize<double[]>(JsonOpts.Default)!,
            r.GetProperty("min").Deserialize<double[]>(JsonOpts.Default)!,
            r.GetProperty("max").Deserialize<double[]>(JsonOpts.Default)!,
            r.GetProperty("cells").GetInt32(),
            r.GetProperty("bestStatistic").GetDouble(),
            r.GetProperty("modes").Deserialize<int[]>(JsonOpts.Default)!
        )).ToList();

        var candidates = res.GetProperty("candidates").EnumerateArray().Select(c => new CandidateView(
            c.GetProperty("candidateId").GetString()!,
            c.GetProperty("regionIndex").GetInt32(),
            c.GetProperty("proportions").Deserialize<double[]>(JsonOpts.Default)!,
            c.GetProperty("residuals").Deserialize<double[]>(JsonOpts.Default)!,
            c.GetProperty("statistic").GetDouble(),
            c.GetProperty("modes").Deserialize<int[]>(JsonOpts.Default)!,
            c.GetProperty("totalCovariance").Deserialize<double[][]>(JsonOpts.Default)!
        )).ToList();

        var diags = res.GetProperty("diagnostics").Deserialize<List<string>>(JsonOpts.Default) ?? new();
        string cond = res.GetProperty("conditionNumber").ValueKind == JsonValueKind.String
            ? res.GetProperty("conditionNumber").GetString()!
            : res.GetProperty("conditionNumber").GetDouble().ToString("0.##");
        var decision = _store.LatestDecision(branchId, trialId);

        return new TrialView(
            t.TrialId, branchId, t.SampleId, sampleName, tracers, sourceIds, sourceNames,
            obs, mc, fracRow.Id, fracLabel, shifts,
            res.GetProperty("feasible").GetBoolean(),
            res.GetProperty("insideMeanHull").GetBoolean(),
            res.GetProperty("identifiabilityAdequate").GetBoolean(),
            cond,
            res.GetProperty("priorInfeasible").GetBoolean(),
            res.GetProperty("threshold").GetDouble(),
            res.GetProperty("bestStatistic").GetDouble(),
            regions, candidates, diags,
            Math.Clamp(selectedCandidate, 0, Math.Max(0, candidates.Count - 1)),
            sources, t.CreatedEventId, t.TrialVersion, t.Status,
            Invalidation.AllRefs(t),
            decision?.Kind.ToString().ToLowerInvariant(), decision?.Comment);
    }

}

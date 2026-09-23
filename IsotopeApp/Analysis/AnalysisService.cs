using System.Text.Json;
using IsotopeApp.Cases;
using IsotopeApp.Domain;
using IsotopeApp.Storage;

namespace IsotopeApp.Analysis;

/// <summary>
/// Turns a case-pack document into stored raw records + a solved trial,
/// recording machine-readable events for every rule judgment. This is the
/// derived layer: raw tokens are stored first and never cleaned destructively.
/// </summary>
public sealed class AnalysisService
{
    private readonly Store _store;
    public AnalysisService(Store store) { _store = store; }

    public sealed record RunOutcome(
        string BatchId,
        string SampleId, string SampleVersion,
        IReadOnlyList<(string SourceId, string Version)> Sources,
        string FractionationId, string FractionationVersion,
        string? PriorId, string? PriorVersion,
        string? CovarianceVersion,
        SolverResult Result,
        IReadOnlyList<ProjectionEvent> Events);

    public RunOutcome RunCase(CasePack pack, string branchId, string trialId, string? priorOverride = null)
    {
        var root = pack.Doc.RootElement;
        var events = new List<ProjectionEvent>();

        // ---------- parse, preserving raw representations ----------
        var tracers = root.GetProperty("tracers").EnumerateArray()
            .Select(t => t.GetProperty("code").GetString()!).ToArray();
        var sampleEl = root.GetProperty("sample");
        string sampleId = sampleEl.GetProperty("sampleId").GetString()!;
        string sampleName = sampleEl.GetProperty("name").GetString()!;
        var values = new double[tracers.Length];
        var valueRecords = new List<IsotopeValue>();
        var diags = new List<ImportDiagnostic>();
        var rawVals = sampleEl.GetProperty("raw");
        var valuesEl = sampleEl.GetProperty("values");
        for (int i = 0; i < tracers.Length; i++)
        {
            var tokenProp = rawVals.GetProperty(tracers[i]);
            string rawToken = tokenProp.GetString()!;
            var numEl = valuesEl[i];
            if (numEl.ValueKind == JsonValueKind.Null || numEl.ValueKind == JsonValueKind.Undefined)
            {
                values[i] = double.NaN;
                valueRecords.Add(new IsotopeValue(tracers[i], rawToken, null,
                    MeasureUnit.Parse(ExtractUnit(rawToken)), "missing at source; retained"));
                diags.Add(new ImportDiagnostic("warn", sampleId, tracers[i],
                    $"missing isotope dimension kept in place: '{rawToken}'"));
            }
            else
            {
                values[i] = numEl.GetDouble();
                valueRecords.Add(new IsotopeValue(tracers[i], rawToken, values[i],
                    MeasureUnit.Parse(ExtractUnit(rawToken)), null));
            }
        }
        var covEl = sampleEl.GetProperty("measurementCovariance");
        var covRows = covEl.GetProperty("matrix").Deserialize<double[][]>(JsonOpts.Default)!;
        var covTracers = covEl.GetProperty("tracers").Deserialize<string[]>(JsonOpts.Default)!;
        var cov = new MeasurementCovariance(covTracers, ToMatrix(covRows),
            covEl.GetProperty("rawToken").GetString()!);

        var sample = new SampleRecord
        {
            SampleId = sampleId, Name = sampleName,
            Values = valueRecords, Covariance = cov,
            RawJson = sampleEl.GetRawText()
        };

        var sourceRecords = new List<SourceRecord>();
        foreach (var s in root.GetProperty("sources").EnumerateArray())
        {
            var modes = new List<SourceMode>();
            foreach (var m in s.GetProperty("modes").EnumerateArray())
            {
                var mean = m.GetProperty("mean").Deserialize<double[]>(JsonOpts.Default)!;
                var mc = m.GetProperty("covariance").Deserialize<double[][]>(JsonOpts.Default)!;
                double weight = m.TryGetProperty("weight", out var w) ? w.GetDouble() : 1.0;
                modes.Add(new SourceMode(mean, ToMatrix(mc), weight));
            }
            sourceRecords.Add(new SourceRecord(
                s.GetProperty("sourceId").GetString()!,
                s.GetProperty("name").GetString()!,
                tracers, modes,
                s.GetProperty("meansRaw").GetString()!,
                s.GetProperty("covarianceRaw").GetString()!));
        }

        var fracEl = root.GetProperty("fractionation");
        var shifts = new Dictionary<string, double>();
        foreach (var p in fracEl.GetProperty("shifts").EnumerateObject())
            shifts[p.Name] = p.Value.GetDouble();
        var frac = new FractionationVersion(
            fracEl.GetProperty("fractionationId").GetString()!,
            fracEl.GetProperty("label").GetString()!,
            shifts, fracEl.GetProperty("basis").GetString()!,
            fracEl.GetProperty("rawToken").GetString()!);

        PriorAssumption? prior = null;
        JsonElement? priorEl = root.TryGetProperty("prior", out var pe) && pe.ValueKind != JsonValueKind.Null ? pe : null;
        if (priorEl is JsonElement pe2)
        {
            var priorBoundsMap = new Dictionary<string, (double, double)>();
            foreach (var b in pe2.GetProperty("bounds").EnumerateObject())
            {
                var arr = b.Value.Deserialize<double[]>(JsonOpts.Default)!;
                priorBoundsMap[b.Name] = (arr[0], arr[1]);
            }
            prior = new PriorAssumption(
                pe2.GetProperty("priorId").GetString()!,
                pe2.GetProperty("kind").GetString()!,
                priorBoundsMap, pe2.GetProperty("rawToken").GetString()!);
        }

        // ---------- transactional import (raw bytes first) ----------
        byte[] rawBytes = System.Text.Encoding.UTF8.GetBytes(pack.RawText);
        var rows = new List<object> { sample };
        rows.AddRange(sourceRecords);
        rows.AddRange(diags.Cast<object>());
        var batch = _store.ImportBatch(branchId, rawBytes, "application/json", pack.Doc, rows);

        string? supersedes = fracEl.TryGetProperty("supersedes", out _) ? null : null;
        _store.SaveFractionation(branchId, frac, supersedes);
        if (prior is not null) _store.SavePrior(branchId, prior, null);

        var sampleRow = _store.ActiveSample(branchId, sampleId);
        var covRow = _store.CovarianceForSample(branchId, sampleId);
        var sourceRows = sourceRecords.Select(s => _store.ActiveSource(branchId, s.SourceId)).ToArray();
        var fracRow = _store.ActiveFractionation(branchId, frac.FractionationId);
        var priorRow = prior is null ? null : _store.ActivePrior(branchId, prior.PriorId);

        var deps = new List<DependencyRef>
        {
            new("sample", sampleRow.Id, sampleRow.Version),
            new("fractionation", fracRow.Id, fracRow.Version)
        };
        if (covRow is not null) deps.Add(new DependencyRef("covariance", covRow.Value.Id, covRow.Value.Version));
        foreach (var s in sourceRows) deps.Add(new DependencyRef("source", s.Id, s.Version));
        if (priorRow is not null) deps.Add(new DependencyRef("prior", priorRow.Value.Id, priorRow.Value.Version));

        var importEvent = _store.AppendEvent(branchId, "import_committed", new
        {
            batchId = batch.BatchId, caseId = pack.CaseId,
            rawSha256 = Hashing.Sha256Hex(rawBytes),
            diagnostics = batch.Diagnostics,
            retainedRaw = new[] { "sample.raw", "sample.measurementCovariance.rawToken", "sources[*].meansRaw" }
        }, deps, Array.Empty<string>());
        events.Add(importEvent);

        // ---------- solve ----------
        var solverSources = sourceRecords.Select(s => new SolverSource
        {
            SourceId = s.SourceId, Name = s.Name,
            ModeMeans = s.Modes.Select(m => m.Mean).ToArray(),
            ModeCovariances = s.Modes.Select(m => m.Covariance).ToArray(),
            ModeWeights = s.Modes.Select(m => m.Weight).ToArray()
        }).ToArray();

        int d = tracers.Length;
        var obs = values;
        // missing dimension: drop to present tracers, keep NaN-dimension diagnostics
        var presentIdx = Enumerable.Range(0, d).Where(i => !double.IsNaN(values[i])).ToArray();
        if (presentIdx.Length < d)
        {
            var missEvent = _store.AppendEvent(branchId, "missing_dimension_recorded", new
            {
                sampleId, presentTracers = presentIdx.Select(i => tracers[i]).ToArray(),
                missingTracers = Enumerable.Range(0, d).Where(i => double.IsNaN(values[i])).Select(i => tracers[i]).ToArray(),
                action = "retained_in_place_not_dropped"
            }, deps, Array.Empty<string>());
            events.Add(missEvent);
        }
        var effTracers = presentIdx.Select(i => tracers[i]).ToArray();
        var effObs = presentIdx.Select(i => values[i]).ToArray();
        var effCov = SubsetCov(cov.Matrix, presentIdx);
        var shift = effTracers.Select(t => frac.Shifts.GetValueOrDefault(t)).ToArray();

        (double Low, double High)[]? bounds = null;
        if (prior is not null)
            bounds = sourceRecords.Select(s =>
                prior.Bounds.TryGetValue(s.SourceId, out var b) ? b : (0.0, 1.0)).ToArray();

        var input = new SolverInput
        {
            TrialId = trialId,
            Tracers = effTracers,
            Observation = effObs,
            MeasurementCovariance = effCov,
            Sources = solverSources,
            FractionationShift = shift,
            ConfidenceLevel = root.TryGetProperty("confidenceLevel", out var cl) ? cl.GetDouble() : 0.95,
            PriorBounds = bounds,
            PriorKind = prior?.Kind
        };
        var result = new MixingSolver().Solve(input);

        // machine-readable judgment events for the domain rules
        EmitJudgmentEvents(branchId, result, deps, events, trialId, cov.IsCorrelated, presentIdx.Length, d);

        var resultDoc = JsonDocument.Parse(Hashing.Canonical(new
        {
            feasible = result.Feasible,
            insideMeanHull = result.InsideMeanHull,
            identifiabilityAdequate = result.IdentifiabilityAdequate,
            conditionNumber = double.IsInfinity(result.ConditionNumber) ? (object)"Infinity" : result.ConditionNumber,
            smallestSingularValue = result.SmallestSingularValue,
            threshold = result.StatisticThreshold,
            bestStatistic = result.UnconstrainedBestStatistic,
            priorInfeasible = result.PriorInfeasible,
            gridSize = result.GridSize,
            subdivisions = result.Subdivisions,
            regions = result.Regions.Select(r => new
            {
                index = r.ComponentIndex,
                center = r.Center, min = r.MinProportion, max = r.MaxProportion,
                cells = r.GridCellCount, best = r.BestPoint, bestStatistic = r.BestStatistic,
                modes = r.ModeSelection, mass = r.RegionMass
            }),
            candidates = result.Candidates.Select(c => new
            {
                c.CandidateId, c.RegionIndex, proportions = c.Proportions,
                residuals = c.Residuals, statistic = c.Statistic, modes = c.ModeSelection,
                totalCovariance = ToJagged(c.TotalCovariance)
            }),
            diagnostics = result.Diagnostics,
            sources = sourceRecords.Select(s => s.SourceId),
            tracers = effTracers,
            observation = effObs
        }));

        var solveEvent = _store.AppendEvent(branchId, "analysis_completed", new
        {
            trialId, feasible = result.Feasible, regions = result.Regions.Count,
            candidates = result.Candidates.Count,
            identifiabilityAdequate = result.IdentifiabilityAdequate,
            fractionationId = frac.FractionationId,
            solverVersion = "mixing-solver-1"
        }, deps, Array.Empty<string>());
        events.Add(solveEvent);

        var stored = new StoredTrial(
            trialId, 0, branchId, sampleId, sampleRow.Version,
            fracRow.Id, fracRow.Version, priorRow?.Id, priorRow?.Version,
            sourceRows.Select(s => new DependencyRef("source", s.Id, s.Version)).ToList(),
            covRow?.Version, resultDoc, solveEvent.EventId, solveEvent.OccurredAt,
            result.Feasible ? "feasible" : "infeasible");
        var saved = _store.SaveTrial(stored);

        return new RunOutcome(
            batch.BatchId, sampleId, sampleRow.Version,
            sourceRows.Select(s => (s.Id, s.Version)).ToList(),
            fracRow.Id, fracRow.Version,
            priorRow?.Id, priorRow?.Version,
            covRow?.Version, result, events);
    }

    private void EmitJudgmentEvents(string branch, SolverResult r, List<DependencyRef> deps,
        List<ProjectionEvent> events, string trialId, bool correlated, int effD, int totalD)
    {
        if (!r.IdentifiabilityAdequate)
            events.Add(_store.AppendEvent(branch, "identifiability_flagged", new
            {
                trialId, field = "identifiabilityAdequate", value = false,
                reasonCode = "near_collinear_endmembers",
                conditionNumber = double.IsInfinity(r.ConditionNumber) ? (object)"Infinity" : r.ConditionNumber,
                rule = "explicit_insufficient_identifiability_report"
            }, deps, Array.Empty<string>()));
        if (!r.InsideMeanHull)
            events.Add(_store.AppendEvent(branch, "sample_outside_envelope", new
            {
                trialId, field = "insideMeanHull", value = false,
                rule = "no_single_point_fabrication"
            }, deps, Array.Empty<string>()));
        if (correlated)
            events.Add(_store.AppendEvent(branch, "correlated_error_propagated", new
            {
                trialId, rule = "full_covariance_propagation",
                offDiagonalKept = true
            }, deps, Array.Empty<string>()));
        if (effD < totalD)
            events.Add(_store.AppendEvent(branch, "missing_dimension_recorded", new
            {
                trialId, effectiveDimensions = effD, totalDimensions = totalD,
                rule = "missing_value_kept_in_place"
            }, deps, Array.Empty<string>()));
        if (r.Regions.Count > 1)
            events.Add(_store.AppendEvent(branch, "multiple_regions_detected", new
            {
                trialId, regionCount = r.Regions.Count,
                rule = "all_disconnected_regions_retained_as_candidates"
            }, deps, Array.Empty<string>()));
        if (r.PriorInfeasible)
            events.Add(_store.AppendEvent(branch, "infeasible_prior_rejected", new
            {
                trialId, field = "priorInfeasible", value = true,
                reasonCode = "bounds_empty_intersection_with_simplex",
                rule = "prior_checked_not_forced"
            }, deps, Array.Empty<string>()));
    }

    internal static double[][] ToJagged(double[,] m)
    {
        int n = m.GetLength(0), k = m.GetLength(1);
        var rows = new double[n][];
        for (int i = 0; i < n; i++) { rows[i] = new double[k]; for (int j = 0; j < k; j++) rows[i][j] = m[i, j]; }
        return rows;
    }

    private static string ExtractUnit(string rawToken)
    {
        int idx = rawToken.IndexOf(' ');
        return idx < 0 ? "" : rawToken[(idx + 1)..];
    }

    public static double[,] ToJaggedReverse(double[][] rows) => ToMatrix(rows);

    internal static double[,] ToMatrix(double[][] rows)
    {
        var m = new double[rows.Length, rows[0].Length];
        for (int i = 0; i < rows.Length; i++)
            for (int j = 0; j < rows[0].Length; j++) m[i, j] = rows[i][j];
        return m;
    }

    internal static double[,] SubsetCov(double[,] cov, int[] idx)
    {
        var m = new double[idx.Length, idx.Length];
        for (int a = 0; a < idx.Length; a++)
            for (int b = 0; b < idx.Length; b++)
                m[a, b] = cov[idx[a], idx[b]];
        return m;
    }
}

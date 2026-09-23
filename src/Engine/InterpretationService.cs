using System.Text.Json.Nodes;
using IsotopeMixing.Cases;
using IsotopeMixing.Solver;
using IsotopeMixing.Storage;

namespace IsotopeMixing.Engine;

/// <summary>
/// Derived interpretation built ONLY from raw records (immutable) plus branch-local
/// revision events (which reference, and never overwrite, the original raw bytes).
/// </summary>
public sealed class InterpretationService
{
    private readonly Repository _repo;
    public InterpretationService(Repository repo) { _repo = repo; }

    public sealed record Revision(string Kind, string TargetUid, JsonNode Payload, long Seq, string EventId);

    public sealed class ProjectedCase
    {
        public required List<IsoAxis> Axes;
        public required Dictionary<string, EndmemberFile> Endmembers;
        public required Dictionary<string, EndmemberSetFile> Sets;
        public required Dictionary<string, SampleFile> Samples;
        public required Dictionary<string, CovarianceFile> Covariances;
        public required Dictionary<string, FractionationFile> Fractionations;
        public required Dictionary<string, PriorFile> Priors;
        public required Dictionary<string, ScenarioFile> Scenarios;
        public required List<Revision> Revisions;
        public required List<ImportDiagnostic> Diagnostics;
    }

    public ProjectedCase Project(string batchId, IReadOnlyList<EventRow> branchEvents)
    {
        var raws = _repo.ReadRawByBatch(batchId).ToDictionary(r => (r.kind, r.uid), r => r.raw);
        var axes = Deser<List<AxisFile>>(raws[("axis", "axes")]);
        var ems = Deser<List<EndmemberFile>>(raws[("endmember", "endmembers")]).ToDictionary(e => e.Uid);
        var sets = Deser<List<EndmemberSetFile>>(raws[("endmember_set", "endmember_sets")]).ToDictionary(e => e.Uid);
        var covs = Deser<List<CovarianceFile>>(raws[("covariance", "covariances")]).ToDictionary(e => e.Uid);
        var fracs = Deser<List<FractionationFile>>(raws[("fractionation", "fractionations")]).ToDictionary(e => e.Uid);
        var priors = Deser<List<PriorFile>>(raws[("prior", "priors")]).ToDictionary(e => e.Uid);
        var scenarios = ReadScenarios(batchId);
        var samples = new Dictionary<string, SampleFile>();
        var diagnostics = new List<ImportDiagnostic>();
        foreach (var (kind, uid, raw, status, diag) in _repo.ReadRawByBatch(batchId).Where(r => r.kind == "sample"))
        {
            samples[uid] = Deser<SampleFile>(raw);
            foreach (var d in Json.Deser<List<ImportDiagnostic>>(diag))
                diagnostics.Add(d);
        }
        var revisions = branchEvents
            .Where(e => e.EventType is "source_revised" or "parameter_revised")
            .Select(e => new Revision(e.EventType == "source_revised" ? "source" : "parameter",
                e.Payload["target"]!.GetValue<string>(), e.Payload, e.Seq, e.EventId))
            .ToList();

        return new ProjectedCase
        {
            Axes = axes.Select(a => new IsoAxis(a.Key, a.Label, a.Unit, a.Datum)).ToList(),
            Endmembers = ems, Sets = sets, Samples = samples, Covariances = covs,
            Fractionations = fracs, Priors = priors, Scenarios = scenarios,
            Revisions = revisions, Diagnostics = diagnostics
        };
    }

    private Dictionary<string, ScenarioFile> ReadScenarios(string batchId)
    {
        var dict = new Dictionary<string, ScenarioFile>();
        foreach (var (kind, uid, raw, status, diag) in _repo.ReadRawByBatch(batchId).Where(r => r.kind == "scenario"))
            dict[uid] = Deser<ScenarioFile>(raw);
        return dict;
    }

    private static T Deser<T>(string raw) => Json.Deser<T>(raw);

    public MixingScenario BuildScenario(ProjectedCase pc, ScenarioFile sc, double confidence)
    {
        var axes = pc.Axes;
        var setFile = pc.Sets[sc.EndmemberSet];
        var members = new List<EndmemberSource>();
        foreach (var uid in setFile.Members)
        {
            var f = pc.Endmembers[uid];
            var mean = axes.Select(a => f.Means.TryGetValue(a.Key, out var v) ? v : double.NaN).ToArray();
            var cov = new double[axes.Count][];
            for (int i = 0; i < axes.Count; i++)
            {
                cov[i] = new double[axes.Count];
                double si = f.Sigma.TryGetValue(axes[i].Key, out var s) ? s : 0;
                cov[i][i] = si * si;
            }
            if (f.Correlations is not null)
                foreach (var (aKey, row) in f.Correlations)
                {
                    int ia = axes.FindIndex(a => a.Key == aKey);
                    foreach (var (bKey, rho) in row)
                    {
                        int ib = axes.FindIndex(a => a.Key == bKey);
                        double sa = f.Sigma.GetValueOrDefault(aKey), sb = f.Sigma.GetValueOrDefault(bKey);
                        cov[ia][ib] = rho * sa * sb;
                        cov[ib][ia] = rho * sa * sb;
                    }
                }
            members.Add(new EndmemberSource { Uid = uid, Label = f.Label, Mean = mean, Covariance = cov });
        }
        var set = new EndmemberSet(setFile.Uid, setFile.Label, members, axes);

        var sampleFile = pc.Samples[sc.Sample];
        var values = new double?[axes.Count];
        var units = new string[axes.Count];
        var datums = new string[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            var a = axes[i];
            units[i] = a.Unit; datums[i] = a.Datum;
            if (!sampleFile.Values.TryGetValue(a.Key, out var val)) { values[i] = null; continue; }
            values[i] = val;
            if (sampleFile.UnitsMode == "mixed" && sampleFile.UnitOverrides?.TryGetValue(a.Key, out var u) == true && u != a.Unit)
            {
                var converted = UnitSystem.Convert(a.Key, u, val!.Value, out var diag);
                units[i] = diag is null ? a.Unit : u;
                if (converted is null) values[i] = null; else values[i] = converted;
            }
        }
        var sample = BuildSampleWithCovariance(pc, sc, sampleFile, values!, units, datums);

        FractionationVersion? frac = null;
        var fracUid = EffectiveFractionationUid(pc, sc);
        if (fracUid is not null && pc.Fractionations.TryGetValue(fracUid, out var ff))
        {
            var off = new double[members.Count][];
            for (int j = 0; j < members.Count; j++)
            {
                off[j] = new double[axes.Count];
                for (int a = 0; a < axes.Count; a++)
                    if (ff.Offsets.TryGetValue(members[j].Uid, out var row) && row.TryGetValue(axes[a].Key, out var o))
                        off[j][a] = o;
            }
            frac = new FractionationVersion(ff.Uid, ff.Label, off, axes);
        }

        PriorAssumption? prior = null;
        var priorUid = EffectivePriorUid(pc, sc);
        if (priorUid is not null && pc.Priors.TryGetValue(priorUid, out var pf))
        {
            IReadOnlyList<IReadOnlyList<int>>? groups = pf.StructuralGroups?
                .Select(g => (IReadOnlyList<int>)g.Select(x => x - 1).ToList()).ToList();
            IReadOnlyList<(double, double)?>? boxes = null;
            if (pf.WeightBoxes is not null)
            {
                var b = new List<(double, double)?>();
                for (int j = 0; j < members.Count; j++)
                    b.Add(pf.WeightBoxes.TryGetValue(j + 1, out var rr) ? (rr[0], rr[1]) : null);
                boxes = b;
            }
            prior = new PriorAssumption(pf.Uid, pf.Label, groups, boxes, pf.Note);
        }

        return new MixingScenario(sc.Uid, sc.Label, sample, set, frac, prior, confidence);
    }

    private string? EffectiveFractionationUid(ProjectedCase pc, ScenarioFile sc)
    {
        var rev = pc.Revisions.LastOrDefault(r =>
            r.Kind == "parameter" && r.Payload["scope"]?.GetValue<string>() is "fractionation"
            && (r.Payload["scenario"]?.GetValue<string>() == sc.Uid || r.Payload["target"]?.GetValue<string>() == (sc.Fractionation ?? "")));
        return rev?.Payload["fractionation"]?.GetValue<string>() ?? sc.Fractionation;
    }

    private string? EffectivePriorUid(ProjectedCase pc, ScenarioFile sc)
    {
        var rev = pc.Revisions.LastOrDefault(r =>
            r.Kind == "parameter" && r.Payload["scope"]?.GetValue<string>() is "prior"
            && r.Payload["scenario"]?.GetValue<string>() == sc.Uid);
        return rev?.Payload["prior"]?.GetValue<string>() ?? sc.Prior;
    }

    private SampleObservation BuildSampleWithCovariance(ProjectedCase pc, ScenarioFile sc, SampleFile sampleFile,
        double?[] values, string[] units, string[] datums)
    {
        int n = pc.Axes.Count;
        var cov = new double?[n][];
        for (int i = 0; i < n; i++) cov[i] = new double?[n];
        var covUid = sc.Covariance ?? sampleFile.Covariance;
        if (covUid is not null && pc.Covariances.TryGetValue(covUid, out var cf))
        {
            for (int i = 0; i < cf.Axes.Count; i++)
            {
                int ri = pc.Axes.FindIndex(a => a.Key == cf.Axes[i]);
                for (int j = 0; j < cf.Axes.Count; j++)
                {
                    int rj = pc.Axes.FindIndex(a => a.Key == cf.Axes[j]);
                    if (ri >= 0 && rj >= 0) cov[ri][rj] = cf.Matrix[i][j];
                }
            }
        }
        else
        {
            for (int i = 0; i < n; i++)
                if (values[i].HasValue) cov[i][i] = 1.0;
        }
        // Source revisions can replace a sample value or covariance cell; originals stay in raw_records.
        foreach (var rev in pc.Revisions.Where(r => r.Kind == "source"))
        {
            var p = rev.Payload;
            if (p["target"]?.GetValue<string>() == sampleFile.Uid && p["axis"] is not null)
            {
                int ai = pc.Axes.FindIndex(a => a.Key == p["axis"]!.GetValue<string>());
                if (ai >= 0)
                {
                    if (p["value"] is not null) values[ai] = p["value"]!.GetValue<double>();
                    if (p["sigma"] is not null && cov[ai] is not null) cov[ai][ai] = Math.Pow(p["sigma"]!.GetValue<double>(), 2);
                }
            }
            if (p["target"]?.GetValue<string>() == covUid && p["sigma"] is not null && p["axis"] is not null)
            {
                int ai = pc.Axes.FindIndex(a => a.Key == p["axis"]!.GetValue<string>());
                if (ai >= 0) cov[ai][ai] = Math.Pow(p["sigma"]!.GetValue<double>(), 2);
            }
        }
        return new SampleObservation { Uid = sampleFile.Uid, Label = sampleFile.Label, Values = values, Covariance = cov, Units = units, Datums = datums };
    }

    /// <summary>Fingerprint of a raw source as currently seen by a branch (raw sha, or raw sha + revision).</summary>
    public string Fingerprint(string rawUid, string batchId, IEnumerable<Revision> revisions)
    {
        var raw = _repo.AllRawRecords().FirstOrDefault(r => r.Uid == rawUid && r.BatchId == batchId);
        var baseFp = raw?.ByteSha256 ?? "missing";
        var relevant = revisions.Where(r => r.TargetUid == rawUid).OrderBy(r => r.Seq)
            .Select(r => r.EventId + ":" + r.Payload.ToJsonString());
        return baseFp + "|" + string.Join("||", relevant);
    }
}

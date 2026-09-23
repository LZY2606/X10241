using System;
using System.Linq;
using System.Text.Json;
using IsoMix.App;
using IsoMix.Cases;
using IsoMix.Domain;
using IsoMix.Storage;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace IsoMix.Tests;

public class ScenarioTests
{
    private readonly ITestOutputHelper _o;
    public ScenarioTests(ITestOutputHelper o) => _o = o;

    private sealed class Scope : IDisposable
    {
        public Store Store { get; }
        public AnalysisService Analysis { get; }
        public CasePack Pack { get; }
        private readonly string _path;
        public Scope()
        {
            _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"isoscen_{Guid.NewGuid():N}.db");
            Store = new Store("Data Source=" + _path);
            Pack = CaseLoader.LoadEmbedded();
            Store.ImportCasePack(Pack);
            Analysis = new AnalysisService(Store);
        }
        public void Dispose() { Store.Dispose(); foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" }) if (System.IO.File.Exists(f)) System.IO.File.Delete(f); SqliteConnection.ClearAllPools(); }
    }

    [Fact]
    public void All_seven_fixed_scenarios_are_present()
    {
        using var s = new Scope();
        var ids = s.Pack.Scenarios.Select(x => x.Id).ToList();
        Assert.Contains("scenario.near-collinear", ids);
        Assert.Contains("scenario.outside-envelope", ids);
        Assert.Contains("scenario.correlated-errors", ids);
        Assert.Contains("scenario.missing-axis", ids);
        Assert.Contains("scenario.fractionation-switch", ids);
        Assert.Contains("scenario.multiple-regions", ids);
        Assert.Contains("scenario.infeasible-prior", ids);
    }

    [Fact]
    public void Near_collinear_reports_unidentifiability_and_keeps_candidates()
    {
        using var s = new Scope();
        var run = s.Analysis.Prepare("hyp.s1.collinear.none");
        var r = run.Result;
        Assert.False(r.Identifiable);
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdNearCollinear);
        // 即使不可识别，比例仍严格和为一非负
        Assert.All(r.PointEstimate!, v => Assert.InRange(v, -1e-9, 1.0 + 1e-9));
        Assert.Equal(1.0, r.PointEstimate!.Sum(), 8);
    }

    [Fact]
    public void Outside_envelope_and_infeasible_prior_block_feasibility()
    {
        using var s = new Scope();
        var outside = s.Analysis.Prepare("hyp.s1.std.none");
        // 用场景修订把样品改到包络外
        var sample = JsonSerializer.Deserialize<SampleSpec>(
            s.Pack.Scenarios.First(x => x.Id == "scenario.outside-envelope").AmendJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        s.Store.AmendRecord(Store.BaselineBranch, s.Store.GetHead(Store.BaselineBranch)!, "sample.estuary.s1", sample, "sample");
        var runOut = s.Analysis.Prepare("hyp.s1.std.none");
        Assert.False(runOut.Result.SampleInsideEnvelope);
        Assert.Contains(runOut.Result.Diagnostics, d => d.Code == MixingMath.IdSampleOutsideEnvelope);

        var infeasible = s.Analysis.Prepare("hyp.s1.std.none.infeasible");
        Assert.True(infeasible.Result.PriorInfeasible);
        Assert.False(infeasible.Result.Feasible);
        Assert.Contains(infeasible.Result.Diagnostics, d => d.Code == MixingMath.IdPriorInfeasible);
    }

    [Fact]
    public void Correlated_covariance_is_propagated()
    {
        using var s = new Scope();
        // 将样品协方差切换到相关误差版本（局部修订）
        var sample = s.Pack.Samples.First(x => x.Id == "sample.estuary.s1");
        sample.Covariance = "cov.correlated.v1";
        s.Store.AmendRecord(Store.BaselineBranch, s.Store.GetHead(Store.BaselineBranch)!, sample.Id, sample, "sample");
        var r = s.Analysis.Prepare("hyp.s1.std.none").Result;
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdCorrelatedErrors);
    }

    [Fact]
    public void Missing_axis_reduces_dimension_without_filling()
    {
        using var s = new Scope();
        var r = s.Analysis.Prepare("hyp.s2.std.none").Result;
        Assert.Single(r.Axes);
        Assert.Equal("d13C", r.Axes[0]);
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdMissingAxes);
    }

    [Fact]
    public void Fractionation_switch_changes_effective_endmembers()
    {
        using var s = new Scope();
        var v1 = s.Analysis.Prepare("hyp.s1.std.none").Result;
        var v2 = s.Analysis.Prepare("hyp.s1.std.trophic").Result;
        // v2 有效端元坐标应整体加上营养级分馏（氮 +3.2）
        Assert.Equal(v1.EndmemberMatrix[1, 0] + 3.2, v2.EndmemberMatrix[1, 0], 6);
        Assert.Equal(v1.EndmemberMatrix[0, 0] + 1.0, v2.EndmemberMatrix[0, 0], 6);
    }

    [Fact]
    public void Multiple_regions_yields_two_components_all_retained()
    {
        using var s = new Scope();
        var run = s.Analysis.Prepare("hyp.s1.std.none", multiRegionDemo: true);
        _o.WriteLine($"components={run.Result.Components.Count}");
        Assert.Equal(2, run.Result.Components.Count);
        Assert.Contains(run.Result.Diagnostics, d => d.Code == MixingMath.IdMultipleRegions);
    }

    [Fact]
    public void Recorded_run_emits_machine_readable_identifiability_and_provenance_chain()
    {
        using var s = new Scope();
        var head = s.Store.GetHead(Store.BaselineBranch)!;
        s.Analysis.RecordRun(Store.BaselineBranch, head, "hyp.s1.collinear.none");
        var events = s.Store.EventsOnBranch(Store.BaselineBranch);
        Assert.Contains(events, e => e.Type == EventTypes.IdentifiabilityFlagged);
        var flag = events.First(e => e.Type == EventTypes.IdentifiabilityFlagged);
        Assert.Contains(flag.Deps, d => d == "emset.collinear.v1");
        Assert.Contains(flag.Deps, d => d == "sample.estuary.s1");
        // 来源 blob 可回读
        Assert.All(flag.SourceBlobs, sha => Assert.NotNull(s.Store.BlobBytes(sha)));
        Assert.Contains(flag.PayloadJson, "no-clip-then-renormalize");
        Assert.Contains(flag.PayloadJson, "covariance_propagation");
    }

    [Fact]
    public void Local_amendment_invalidates_scoped_dependencies_only()
    {
        using var s = new Scope();
        // 两个使用不同样品/分馏的分析
        s.Analysis.RecordRun(Store.BaselineBranch, s.Store.GetHead(Store.BaselineBranch)!, "hyp.s1.std.none");
        s.Analysis.RecordRun(Store.BaselineBranch, s.Store.GetHead(Store.BaselineBranch)!, "hyp.s1.std.trophic");
        int before = s.Store.EventsOnBranch(Store.BaselineBranch).Count(e => e.Type == EventTypes.AnalysisComputed);
        Assert.Equal(2, before);

        var frac = s.Pack.Fractionations.First(f => f.Id == "frac.trophic.v2");
        frac.Factors[1].Delta = "3.5"; // 只改 v2 分馏
        var res = s.Store.AmendRecord(Store.BaselineBranch, s.Store.GetHead(Store.BaselineBranch)!, frac.Id, frac, "fractionation");

        // 只有依赖 frac.trophic.v2 的分析应失效；hyp.s1.std.none 不失效
        var invalidated = res.InvalidatedEvents
            .Select(id => s.Store.GetEvent(id))
            .Where(e => e!.Type == EventTypes.AnalysisComputed).ToList();
        Assert.Single(invalidated);
        var payload = invalidated[0]!.PayloadJson;
        Assert.Contains("hyp.s1.std.trophic", payload);
    }
}

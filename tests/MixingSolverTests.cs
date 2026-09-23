using IsotopeMixing.Solver;
using Xunit;

namespace IsotopeMixing.Tests;

public class MixingSolverTests
{
    private static readonly IsoAxis C = new("d13C", "d13C", "permil_VPDB", "VPDB");
    private static readonly IsoAxis N = new("d15N", "d15N", "permil_AIR", "AIR");

    private static EndmemberSource Em(string uid, string label, double c, double n)
        => new() { Uid = uid, Label = label, Mean = new[] { c, n }, Covariance = new[] { new[] { 0.25, 0.0 }, new[] { 0.0, 0.25 } } };

    private static SampleObservation Sample(string uid, double? c, double? n, double?[][]? cov = null)
        => new()
        {
            Uid = uid, Label = uid,
            Values = new[] { c, n },
            Covariance = cov ?? new[] { new double?[] { 0.25, 0.0 }, new double?[] { 0.0, 0.25 } },
            Units = new[] { "permil_VPDB", "permil_AIR" },
            Datums = new[] { "VPDB", "AIR" }
        };

    private static EndmemberSet Set3() => new("ems", "3EM", new[]
    {
        Em("a", "A", -18, 12), Em("b", "B", -28, 3), Em("c", "C", -22, 9)
    }, new[] { C, N });

    private static MixingScenario Scenario(SampleObservation s, EndmemberSet set, PriorAssumption? prior = null,
        FractionationVersion? frac = null)
        => new("sc", "sc", s, set, frac, prior, 0.95);

    [Fact]
    public void InteriorSample_GivesFeasibleRegionAndWeightsSumToOne()
    {
        var r = new MixingSolver().Solve(Scenario(Sample("s", -23, 7.5), Set3())).Single();
        Assert.True(r.Feasible);
        Assert.NotNull(r.BestWeights);
        Assert.Equal(1.0, r.BestWeights!.Sum(w => w), 6);
        Assert.All(r.BestWeights, w => Assert.InRange(w, -1e-9, 1 + 1e-9));
        Assert.NotNull(r.Polygon);
        Assert.True(r.Polygon!.Area > 0);
        Assert.Contains(r.Assessments, a => a.Code == "SUM_TO_ONE_NONNEGATIVE" && a.Satisfied);
        Assert.Contains(r.Assessments, a => a.Code == "NEAR_COLLINEAR_ENDMEMBERS" && a.Satisfied);
    }

    [Fact]
    public void FarSample_IsReportedOutsideEnvelope_NotForcedIntoMix()
    {
        var r = new MixingSolver().Solve(Scenario(Sample("s", -5, 25), Set3())).Single();
        Assert.False(r.Feasible);
        Assert.Equal("infeasible", r.Status);
        Assert.Contains(r.Assessments, a => a.Code == "SAMPLE_WITHIN_ENVELOPE" && !a.Satisfied);
        // still a best-effort QP point on the simplex, never a clipped renormalization
        Assert.NotNull(r.BestWeights);
        Assert.Equal(1.0, r.BestWeights!.Sum(), 6);
    }

    [Fact]
    public void CollinearEndmembers_ReportUnidentifiable_AndKeepWitnesses()
    {
        var set = new EndmemberSet("col", "col", new[]
        {
            Em("a", "A", -30, 2), Em("b", "B", -25, 7), Em("c", "C", -20, 12)
        }, new[] { C, N });
        var r = new MixingSolver().Solve(Scenario(Sample("s", -22.5, 9.5), set)).Single();
        Assert.Equal("underidentified", r.Status);
        Assert.Equal("unidentifiable", r.Identifiability);
        Assert.NotNull(r.Unresolved);
        Assert.NotEmpty(r.Unresolved!.WitnessWeights);
        Assert.All(r.Unresolved.WitnessWeights, w =>
        {
            Assert.Equal(1.0, w.Sum(), 6);
            Assert.All(w, x => Assert.InRange(x, -1e-9, 1 + 1e-9));
        });
        Assert.Contains(r.Assessments, a => a.Code == "NEAR_COLLINEAR_ENDMEMBERS" && !a.Satisfied);
    }

    [Fact]
    public void CorrelatedCovariance_IsAcceptedAndChangesGeometryVsDiagonal()
    {
        var corr = new[] { new double?[] { 0.36, 0.306 }, new double?[] { 0.306, 0.36 } };
        var diag = new[] { new double?[] { 0.36, 0.0 }, new double?[] { 0.0, 0.36 } };
        var sCorr = Sample("s", -22.4, 8.6, corr);
        var sDiag = Sample("s", -22.4, 8.6, diag);
        var outCorr = new MixingSolver().Solve(Scenario(sCorr, Set3())).Single();
        var outDiag = new MixingSolver().Solve(Scenario(sDiag, Set3())).Single();
        Assert.Contains(outCorr.Assessments, a => a.Code == "CORRELATED_ERROR_PROPAGATION" && a.Satisfied
            && a.Basis.Contains("off-diagonal"));
        Assert.NotEqual(outCorr.Chi2!.Value, outDiag.Chi2!.Value, 6);
    }

    [Fact]
    public void MissingOneAxis_DegradesToInterval()
    {
        var r = new MixingSolver().Solve(Scenario(Sample("s", -24, null), Set3())).Single();
        Assert.True(r.Feasible);
        Assert.Equal("weak", r.Identifiability);
        Assert.Equal(1, r.Residuals!.Count);
        Assert.NotNull(r.Polygon); // 3 endmembers, 1 axis -> broad region
    }

    [Fact]
    public void ImpossiblePrior_IsInfeasiblePrior_NotSimplexInfeasible()
    {
        var boxes = new (double, double)?[] { (0.6, 1), (0.6, 1), (0.6, 1) };
        var prior = new PriorAssumption("p", "p", null, boxes, "lower bounds sum > 1");
        var r = new MixingSolver().Solve(Scenario(Sample("s", -23, 7), Set3(), prior)).Single();
        Assert.False(r.Feasible);
        Assert.Equal("infeasible", r.Status);
        Assert.Contains(r.Assessments, a => a.Code == "PRIOR_FEASIBILITY" && !a.Satisfied);
    }

    [Fact]
    public void StructuralGroups_ProduceMultipleSeparateFeasibleRegions()
    {
        var groups = new IReadOnlyList<int>[] { new[] { 0, 2 } };
        var prior = new PriorAssumption("p", "p", groups, null, "marine or agriculture");
        var rs = new MixingSolver().Solve(Scenario(Sample("s", -25, 3.6), Set3(), prior));
        Assert.Equal(2, rs.Count);
        Assert.All(rs, r => Assert.True(r.Feasible));
        Assert.Equal(2, rs.Select(r => r.StructuralGroupIndex).Distinct().Count());
    }

    [Fact]
    public void FractionationShift_MovesPredictedEndmembersAndChangesChi2()
    {
        var off = new[] { new[] { 0.0, 0.0 }, new[] { 1.2, 1.5 }, new[] { 0.0, 0.0 } };
        var fv = new FractionationVersion("f", "f", off, new[] { C, N });
        var s = Sample("s", -24, 5.2);
        var v1 = new MixingSolver().Solve(Scenario(s, Set3())).Single();
        var v2 = new MixingSolver().Solve(Scenario(s, Set3(), null, fv)).Single();
        Assert.NotEqual(v1.Chi2!.Value, v2.Chi2!.Value, 6);
    }

    [Fact]
    public void QpNeverUsesClipRenormalization_BoundarySolutionStillSumsToOne()
    {
        // With a sample close to endmember A, an unconstrained LS could push w negative;
        // the QP must rest on the face and keep exact sum 1.
        var s = Sample("s", -17, 13);
        var r = new MixingSolver().Solve(Scenario(s, Set3())).Single();
        Assert.Equal(1.0, r.BestWeights!.Sum(), 8);
        Assert.All(r.BestWeights, w => Assert.InRange(w, -1e-8, 1 + 1e-8));
    }
}

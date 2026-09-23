using System.Linq;
using IsoMix.Domain;
using Xunit;

namespace IsoMix.Tests;

public class MixingMathTests
{
    private static MixingInput TriBase(double[,] G, double[] y, double[,]? sigma = null) => new()
    {
        Axes = new() { "d13C", "d15N" },
        Observation = y,
        EndmemberMatrix = G,
        EndmemberIds = new() { "E1", "E2", "E3" },
        EndmemberNames = new() { "陆生C3", "海洋", "底栖" },
        MeasurementCovariance = sigma
    };

    [Fact]
    public void Interior_sample_is_feasible_and_identifiable()
    {
        var G = new double[,] { { -28, -20, -12 }, { 2, 12, 4 } };
        var y = new double[] { -20, 6 };
        var r = MixingMath.Solve(TriBase(G, y));
        Assert.True(r.Identifiable);
        Assert.True(r.SampleInsideEnvelope);
        Assert.True(r.Feasible);
        Assert.NotEmpty(r.Components);
        Assert.All(r.Components.SelectMany(c => new[] { c.Representative!.Fractions }),
            f => { Assert.InRange(f[0], -1e-6, 1.000001); Assert.InRange(f[1], -1e-6, 1.000001); Assert.InRange(f[2], -1e-6, 1.000001); Assert.Equal(1.0, f.Sum(), 6); });
    }

    [Fact]
    public void Near_collinear_endmembers_flag_unidentifiability()
    {
        // 三个端元几乎落在 d13C 轴上（第二维几乎相同）
        var G = new double[,] { { -28, -20, -12 }, { 5.0, 5.001, 5.002 } };
        var y = new double[] { -20, 5.001 };
        var r = MixingMath.Solve(TriBase(G, y));
        Assert.False(r.Identifiable);
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdNearCollinear);
    }

    [Fact]
    public void Sample_outside_envelope_is_flagged()
    {
        var G = new double[,] { { -28, -20, -12 }, { 2, 12, 4 } };
        var y = new double[] { 5, 30 };
        var r = MixingMath.Solve(TriBase(G, y));
        Assert.False(r.SampleInsideEnvelope);
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdSampleOutsideEnvelope);
    }

    [Fact]
    public void Correlated_covariance_is_propagated_and_diagnosed()
    {
        var G = new double[,] { { -28, -20, -12 }, { 2, 12, 4 } };
        var y = new double[] { -20, 7 };
        var sigma = new double[,] { { 0.8, 0.5 }, { 0.5, 0.8 } };
        var r = MixingMath.Solve(TriBase(G, y, sigma));
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdCorrelatedErrors);
        Assert.NotNull(r.BestMahalanobisSquared);
    }

    [Fact]
    public void Infeasible_prior_is_reported_not_forced()
    {
        var G = new double[,] { { -28, -20, -12 }, { 2, 12, 4 } };
        var y = new double[] { -20, 6 };
        var input = TriBase(G, y);
        input.PriorLo = new[] { 0.6, 0.6, 0.0 };
        input.PriorHi = new[] { 1.0, 1.0, 1.0 };
        var r = MixingMath.Solve(input);
        Assert.True(r.PriorInfeasible);
        Assert.False(r.Feasible);
        Assert.Contains(r.Diagnostics, d => d.Code == MixingMath.IdPriorInfeasible);
    }

    [Fact]
    public void Point_estimate_always_sums_to_one_and_nonnegative()
    {
        var G = new double[,] { { -28, -20, -12 }, { 2, 12, 4 } };
        var y = new double[] { 5, 30 }; // outside envelope
        var r = MixingMath.Solve(TriBase(G, y));
        var f = r.PointEstimate!;
        Assert.Equal(1.0, f.Sum(), 9);
        Assert.All(f, v => Assert.InRange(v, -1e-9, 1.000000001));
    }
}

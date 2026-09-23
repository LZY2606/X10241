namespace IsoMix.Domain;

public sealed class SolverOptions
{
    public int GridResolution { get; init; } = 61;
    public double ConfidenceLevel { get; init; } = 0.95;
}

public static class MixingSolver
{
    private static readonly double[] ChiSq95 =
    {
        3.841459, 5.991465, 7.814728, 9.487729, 11.070498, 12.591587
    };

    public static RunResult Solve(NormalizedCase nc, SolverOptions? options = null)
    {
        var opt = options ?? new SolverOptions();
        var flags = new List<string>();
        var diagnostics = new List<string>(nc.Diagnostics);
        var d = nc.Dimensions.Count;
        var k = nc.EndmemberMeans.Length;

        var d2cut = ChiSq95[Math.Min(d - 1, ChiSq95.Length - 1)];

        foreach (var issue in nc.Issues)
            if (!flags.Contains(issue.Reason)) flags.Add(issue.Reason);

        var means = nc.EndmemberMeans;

        var colMeans = new double[d];
        for (var p = 0; p < d; p++)
        {
            for (var j = 0; j < k; j++) colMeans[p] += means[j][p];
            colMeans[p] /= k;
        }

        var a = new double[d][];
        for (var p = 0; p < d; p++)
        {
            a[p] = new double[k];
            for (var j = 0; j < k; j++) a[p][j] = means[j][p] - colMeans[p];
        }

        var sigmaMin = k <= 1 ? 0 : Linalg.MinSingularValue(a);
        var scale = 1.0;
        for (var p = 0; p < d; p++)
            scale = Math.Max(scale, means.Max(m => m[p]) - means.Min(m => m[p]));
        var nearCollinear = k > 1 && (d < k - 1 || sigmaMin < 0.10 * scale);
        if (nearCollinear)
        {
            flags.Add(RunFlags.NearCollinear);
            diagnostics.Add($"min singular value {sigmaMin:G4} of centred endmember matrix below 10% of span scale {scale:G4}");
        }

        var envelope = BuildEnvelope(means, d);
        var outside = !InsideConvexHull(nc.Sample, means, d, k);
        if (outside)
        {
            flags.Add(RunFlags.OutsideEnvelope);
            diagnostics.Add("sample lies outside the endmember convex envelope (exact mixture impossible; error region may still be feasible)");
        }

        var correlated = false;
        for (var p = 0; p < d; p++)
            for (var q = p + 1; q < d; q++)
                if (Math.Abs(nc.SampleCovariance[p][q]) > 1e-15) correlated = true;
        if (correlated) flags.Add(RunFlags.CorrelatedError);

        var points = ScanFeasible(nc, d, k, d2cut, opt, diagnostics, flags);
        var candidates = BuildCandidates(points, nc, d, k, d2cut);
        var status = ComposeStatus(candidates, flags);

        return new RunResult(BuildRunId(nc), status, flags, candidates, envelope, diagnostics);
    }

    private static string BuildRunId(NormalizedCase nc)
    {
        var hc = new HashCode();
        hc.Add(nc.Raw.CaseId);
        hc.Add(nc.Raw.Scenario);
        hc.Add(nc.Raw.Fractionation.Version);
        foreach (var em in nc.Raw.Endmembers) { hc.Add(em.Name); hc.Add(em.Version); }
        foreach (var v in nc.Sample) hc.Add(v);
        foreach (var v in nc.FractionationDelta) hc.Add(v);
        return "run-" + hc.ToHashCode().ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ComposeStatus(List<Candidate> candidates, List<string> flags)
    {
        if (flags.Contains(RunFlags.PriorInfeasible)) return RunStatus.Infeasible;
        if (candidates.Count == 0) return RunStatus.Infeasible;
        if (candidates.Count > 1) return RunStatus.MultiRegion;
        if (flags.Contains(RunFlags.NearCollinear)) return RunStatus.IdentifiabilityInsufficient;
        return RunStatus.Feasible;
    }

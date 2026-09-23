namespace IsotopeMix.Core;

public enum Identifiability { Identified, WeaklyIdentified, NotIdentifiable }

/// <summary>Box prior on proportions, indexed by endmember position.</summary>
public sealed record PriorBox(IReadOnlyList<(double Lo, double Hi)> Bounds)
{
    public bool Admits(double[] p)
    {
        for (int i = 0; i < p.Length; i++)
            if (p[i] < Bounds[i].Lo - 1e-12 || p[i] > Bounds[i].Hi + 1e-12) return false;
        return true;
    }
}

public sealed record MixInput(
    IReadOnlyList<string> Axes,
    double[] Sample,
    double[,] Covariance,
    IReadOnlyList<(string Id, string Name, double[] Values)> Endmembers,
    double ChiSquareCrit,
    PriorBox? Prior);

public sealed record FeasibleRegion(int Index, IReadOnlyList<double[]> Points, double VolumeFraction);

public sealed record MixResult(
    Identifiability Identifiability,
    double ConditionNumber,
    IReadOnlyList<FeasibleRegion> Regions,
    double[]? BestFeasible,
    double[]? ResidualAtBest,
    double MinMahalanobis,
    bool SampleOutsideEnvelope,
    bool PriorInfeasible,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>All legal candidates when the mixture is not uniquely determined.</summary>
    public IEnumerable<double[]> AllCandidates() => Regions.SelectMany(r => r.Points);
}

/// <summary>
/// Feasible-region mixing model. Constraints p_i >= 0, sum p = 1 are enforced by
/// construction of the search grid on the simplex; infeasible unconstrained
/// solutions are reported, never truncated and renormalized into a fake answer.
/// Correlated measurement error is propagated through the full covariance matrix
/// via the Mahalanobis distance.
/// </summary>
public static class MixingModel
{
    public const string RulesVersion = "mix-rules/1.0.0";
    public const double WeakConditionThreshold = 100.0;
    public const double NotIdentifiableConditionThreshold = 1e4;

    public static MixResult Evaluate(MixInput input)
    {
        var diagnostics = new List<string>();
        int d = input.Axes.Count;
        int k = input.Endmembers.Count;

        var e = new double[d, k];
        for (int j = 0; j < k; j++)
            for (int i = 0; i < d; i++) e[i, j] = input.Endmembers[j].Values[i];

        // --- Identifiability from endmember geometry (near-collinearity) ---
        var sv = LinearAlgebra.SingularValues(e);
        double sMax = sv.Length > 0 ? sv[^1] : 0;
        double sMin = sv.Length > 0 ? sv[0] : 0;
        double condition = sMin <= 1e-300 ? double.PositiveInfinity : sMax / sMin;
        var identifiability =
            condition > NotIdentifiableConditionThreshold || k - 1 > d
                ? Identifiability.NotIdentifiable
                : condition > WeakConditionThreshold
                    ? Identifiability.WeaklyIdentified
                    : Identifiability.Identified;
        if (identifiability != Identifiability.Identified)
            diagnostics.Add($"identifiability.insufficient: condition(E)={condition:G3}; endmembers are near-collinear in isotope space");

        // --- Correlated errors: full covariance Mahalanobis distance ---
        double[,] covInv;
        try { covInv = LinearAlgebra.Invert(input.Covariance); }
        catch (InvalidOperationException)
        {
            diagnostics.Add("covariance.singular: measurement covariance is singular");
            covInv = LinearAlgebra.Identity(d);
        }

        // Unconstrained least squares, for diagnostics only (never normalized into an answer).
        var et = LinearAlgebra.Transpose(e);
        var ete = LinearAlgebra.Multiply(et, e);
        double[]? unconstrained = null;
        try
        {
            var eteInv = LinearAlgebra.Invert(ete);
            var ets = LinearAlgebra.Multiply(et, input.Sample);
            unconstrained = LinearAlgebra.Multiply(eteInv, ets);
            bool violates = unconstrained.Any(x => x < -1e-9) || Math.Abs(unconstrained.Sum() - 1) > 1e-6;
            if (violates)
                diagnostics.Add("simplex.unconstrained-violation: unconstrained solution leaves the simplex; it is reported as-is and NOT truncated/renormalized");
        }
        catch (InvalidOperationException)
        {
            diagnostics.Add("simplex.unconstrained-singular: endmember matrix singular, no unique unconstrained solution");
        }

        // --- Feasible region: grid over the simplex, constraints by construction ---
        int divisions = k switch { 2 => 400, 3 => 60, 4 => 18, _ => 10 };
        var grid = SimplexGrid(k, divisions);
        var covPoints = new List<double[]>();
        var priorPoints = new List<double[]>();
        double minChi = double.PositiveInfinity;
        double[]? best = null;
        foreach (var p in grid)
        {
            double chi = Mahalanobis(input.Sample, e, p, covInv);
            if (chi < minChi) { minChi = chi; best = p; }
            if (chi <= input.ChiSquareCrit) covPoints.Add(p);
            if (chi <= input.ChiSquareCrit && (input.Prior?.Admits(p) ?? true)) priorPoints.Add(p);
        }

        bool outsideEnvelope = covPoints.Count == 0;
        if (outsideEnvelope)
            diagnostics.Add($"sample.outside-envelope: no simplex point satisfies chi2<={input.ChiSquareCrit:G3} (min={minChi:G3}); no proportions are fabricated");

        bool priorInfeasible = !outsideEnvelope && input.Prior is not null && priorPoints.Count == 0;
        if (priorInfeasible)
            diagnostics.Add("prior.infeasible: prior box excludes every data-feasible proportion");

        var active = input.Prior is null ? covPoints : priorPoints;
        var regions = Cluster(active, k, divisions)
            .Select((pts, i) => new FeasibleRegion(i, pts, (double)pts.Count / grid.Count))
            .ToList();
        if (regions.Count > 1)
            diagnostics.Add($"feasible.multiple-regions: {regions.Count} disconnected feasible regions; all are kept as candidates");

        double[]? residual = best is null ? null : Residual(input.Sample, e, best);
        return new MixResult(identifiability, condition, regions, outsideEnvelope ? null : best,
            residual, minChi, outsideEnvelope, priorInfeasible, diagnostics);
    }

    public static double Mahalanobis(double[] sample, double[,] e, double[] p, double[,] covInv)
    {
        var r = Residual(sample, e, p);
        var tmp = LinearAlgebra.Multiply(covInv, r);
        double chi = 0;
        for (int i = 0; i < r.Length; i++) chi += r[i] * tmp[i];
        return chi;
    }

    public static double[] Residual(double[] sample, double[,] e, double[] p)
    {
        int d = sample.Length, k = p.Length;
        var r = new double[d];
        for (int i = 0; i < d; i++)
        {
            double mix = 0;
            for (int j = 0; j < k; j++) mix += e[i, j] * p[j];
            r[i] = sample[i] - mix;
        }
        return r;
    }

    /// <summary>Integer barycentric grid on the simplex: every point satisfies p_i>=0, sum=1 exactly.</summary>
    public static List<double[]> SimplexGrid(int k, int divisions)
    {
        var pts = new List<double[]>();
        var buf = new int[k];
        void Rec(int dim, int remaining)
        {
            if (dim == k - 1)
            {
                buf[dim] = remaining;
                var p = new double[k];
                for (int i = 0; i < k; i++) p[i] = (double)buf[i] / divisions;
                pts.Add(p);
                return;
            }
            for (int v = 0; v <= remaining; v++)
            {
                buf[dim] = v;
                Rec(dim + 1, remaining - v);
            }
        }
        Rec(0, divisions);
        return pts;
    }

    /// <summary>Connected components of feasible grid points (adjacency = one grid step moved between two coords).</summary>
    public static List<List<double[]>> Cluster(IReadOnlyList<double[]> points, int k, int divisions)
    {
        int step = 1_000_000 / divisions;
        var key = new Dictionary<string, int>();
        for (int i = 0; i < points.Count; i++) key[GridKey(points[i])] = i;
        var seen = new bool[points.Count];
        var clusters = new List<List<double[]>>();
        for (int i = 0; i < points.Count; i++)
        {
            if (seen[i]) continue;
            var cluster = new List<double[]>();
            var stack = new Stack<int>();
            stack.Push(i);
            seen[i] = true;
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                cluster.Add(points[cur]);
                foreach (var ni in Neighbors(points[cur], step, key))
                    if (!seen[ni])
                    {
                        seen[ni] = true;
                        stack.Push(ni);
                    }
            }
            clusters.Add(cluster);
        }
        return clusters;
    }

    private static string GridKey(double[] p) =>
        string.Join(",", p.Select(x => (int)Math.Round(x * 1_000_000)));

    private static IEnumerable<int> Neighbors(double[] p, int step, Dictionary<string, int> key)
    {
        var units = p.Select(x => (int)Math.Round(x * 1_000_000)).ToArray();
        for (int a = 0; a < p.Length; a++)
            for (int b = 0; b < p.Length; b++)
            {
                if (a == b) continue;
                var nb = (int[])units.Clone();
                nb[a] += step;
                nb[b] -= step;
                if (nb[b] < 0 || nb[a] > 1_000_000) continue;
                if (key.TryGetValue(string.Join(",", nb), out var ni)) yield return ni;
            }
    }
}

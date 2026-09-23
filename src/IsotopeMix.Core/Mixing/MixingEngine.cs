using System;
using System.Collections.Generic;
using System.Linq;
using IsotopeMix.Core.Math;

namespace IsotopeMix.Core.Mixing;

/// <summary>
/// Feasible-set mixing engine. It never returns a single point answer: it reports the
/// whole region of proportions consistent with propagated covariance at the stated level.
/// </summary>
public sealed class MixingEngine
{
    private const double CollinearityRatioThreshold = 1e-2;
    private const double RegionMergeTolerance = 1.5;

    public MixingResult Analyze(MixingInput input)
    {
        var k = input.Sources.Count;
        var q = input.Dimensions.Length;
        var resolution = input.GridResolution ?? DefaultResolution(k);
        var threshold = ChiSquare.Quantile(input.ConfidenceLevel, q);

        var matrix = input.EndmemberMatrix;
        var lo = new double[k];
        var up = new double[k];
        for (var i = 0; i < k; i++)
        {
            var box = input.Priors.FirstOrDefault(b => b.SourceId == input.Sources[i].SourceId);
            lo[i] = box?.Lower ?? 0.0;
            up[i] = box?.Upper ?? 1.0;
        }
        var priorsStructurallyFeasible = lo.Sum() <= 1.0 + 1e-9 && up.Sum() >= 1.0 - 1e-9;

        var events = new List<JudgmentEvent>();
        var singularData = AnalyzeRank(matrix);
        if (singularData.MinRatio < CollinearityRatioThreshold)
        {
            events.Add(new JudgmentEvent(
                "NONIDENTIFIABLE_NEAR_COLLINEAR",
                "warn",
                "Endmembers are (near) collinear in isotope space: proportions are not uniquely identifiable; all legal candidates are retained.",
                input.Sources.Select(s => s.SourceId).ToArray(),
                new Dictionary<string, object?>
                {
                    ["minimumSingularValueRatio"] = singularData.MinRatio,
                    ["nullSpaceDimension"] = singularData.NullDirections.Count > 0 ? 1 : 0,
                    ["nullSpaceDirection"] = singularData.NullDirections,
                    ["ruleVersion"] = Rules.Version,
                }));
        }
        if (q < k - 1)
        {
            events.Add(new JudgmentEvent(
                "STRUCTURALLY_UNDERDETERMINED",
                "warn",
                $"There are {k} sources but only {q} isotope dimensions; at least {k - 1 - q} proportion direction(s) cannot be resolved.",
                input.Dimensions,
                new Dictionary<string, object?>
                {
                    ["sourceCount"] = k,
                    ["dimensionCount"] = q,
                    ["ruleVersion"] = Rules.Version,
                }));
        }
        if (input.DroppedDimensions.Count > 0)
        {
            foreach (var drop in input.DroppedDimensions)
            {
                events.Add(new JudgmentEvent(
                    "MISSING_ISOTOPE_DIMENSION",
                    "info",
                    $"Dimension '{drop.Dimension}' is excluded from inference ({drop.Reason}); the raw record is retained in place.",
                    new[] { drop.TargetRef ?? "sample" },
                    new Dictionary<string, object?>
                    {
                        ["dimension"] = drop.Dimension,
                        ["reason"] = drop.Reason,
                        ["targetRef"] = drop.TargetRef,
                    }));
            }
        }
        var correlationEvents = CorrelationEvents(input);
        events.AddRange(correlationEvents);
        if (OutsideEnvelope(input, out var outsideDim, out var margin))
        {
            events.Add(new JudgmentEvent(
                "SAMPLE_OUTSIDE_ENVELOPE",
                "warn",
                $"Sample lies beyond the endmember convex hull (largest signed margin {margin:G4} in '{outsideDim}') even before covariance is considered.",
                new[] { "sample" }.Concat(input.Sources.Select(s => s.SourceId)).ToArray(),
                new Dictionary<string, object?>
                {
                    ["dimension"] = outsideDim,
                    ["margin"] = margin,
                    ["ruleVersion"] = Rules.Version,
                }));
        }

        var feasible = new List<FeasiblePoint>();
        var feasibleWithPriors = new List<FeasiblePoint>();
        foreach (var proportions in SimplexLattice(k, resolution))
        {
            var point = EvaluatePoint(input, proportions);
            if (point.MahalanobisSquared <= threshold)
            {
                feasible.Add(point);
                var insidePriors = true;
                for (var i = 0; i < k; i++)
                    if (proportions[i] < lo[i] - 1e-9 || proportions[i] > up[i] + 1e-9)
                        insidePriors = false;
                if (insidePriors) feasibleWithPriors.Add(point);
            }
        }

        var components = ClusterComponents(feasible, resolution);
        var componentsWithPriors = ClusterComponents(feasibleWithPriors, resolution);
        var regions = BuildRegions(input, componentsWithPriors, resolution);
        var pointsByComponent = AssignComponentIds(feasibleWithPriors, componentsWithPriors);

        if (components.Count >= 2)
        {
            events.Add(new JudgmentEvent(
                "MULTIPLE_FEASIBLE_REGIONS",
                "warn",
                $"Propagated covariance produces {components.Count} disconnected feasible regions; all regions and their candidates are retained.",
                new[] { "sample" }.Concat(input.Sources.Select(s => s.SourceId)).ToArray(),
                new Dictionary<string, object?>
                {
                    ["regionCount"] = components.Count,
                    ["ruleVersion"] = Rules.Version,
                }));
        }

        var feasibleAtAll = feasible.Count > 0;
        if (!feasibleAtAll)
        {
            events.Add(new JudgmentEvent(
                "NO_FEASIBLE_MIXTURE",
                "error",
                "No simplex point satisfies the covariance-based threshold; closest feasible-set projection is reported as diagnostic, not as an answer.",
                new[] { "sample" }.Concat(input.Sources.Select(s => s.SourceId)).ToArray(),
                new Dictionary<string, object?>
                {
                    ["chiSquareThreshold"] = threshold,
                    ["ruleVersion"] = Rules.Version,
                }));
        }

        if (feasibleAtAll && !priorsStructurallyFeasible)
        {
            events.Add(new JudgmentEvent(
                "PRIOR_INFEASIBLE",
                "error",
                "Prior bounds are structurally incompatible with the unit-sum, non-negativity constraints; no legal proportion set exists under the priors.",
                input.Sources.Select(s => s.SourceId).ToArray(),
                new Dictionary<string, object?>
                {
                    ["sumLowerBounds"] = lo.Sum(),
                    ["sumUpperBounds"] = up.Sum(),
                    ["ruleVersion"] = Rules.Version,
                }));
        }
        else if (feasibleAtAll && feasibleWithPriors.Count == 0)
        {
            events.Add(new JudgmentEvent(
                "PRIOR_INFEASIBLE",
                "error",
                "Covariance-feasible mixtures exist, but none satisfies the prior bounds; priors are rejected rather than quietly clipped away.",
                input.Sources.Select(s => s.SourceId).ToArray(),
                new Dictionary<string, object?>
                {
                    ["chiSquareThreshold"] = threshold,
                    ["ruleVersion"] = Rules.Version,
                }));
        }

        var closest = ClosestPoint(input, lo, up);
        var range = SourceRanges(pointsByComponent);
        if (feasibleAtAll && feasibleWithPriors.Count > 0 &&
            (range.MaxMax - range.MinMin) < Math.Max(1e-3, 3.0 / resolution))
        {
            events.Add(new JudgmentEvent(
                "WEAK_IDENTIFIABILITY",
                "info",
                "Feasible proportions span a broad region for at least one source; point estimates would overstate precision.",
                input.Sources.Select(s => s.SourceId).ToArray(),
                new Dictionary<string, object?>
                {
                    ["sourceRangesMin"] = range.Mins,
                    ["sourceRangesMax"] = range.Maxs,
                }));
        }

        return new MixingResult
        {
            Feasible = feasibleAtAll,
            FeasibleUnderPriors = feasibleWithPriors.Count > 0 && priorsStructurallyFeasible,
            ChiSquareThreshold = threshold,
            DegreesOfFreedom = q,
            GridResolution = resolution,
            Candidates = pointsByComponent,
            Regions = regions,
            ClosestProportions = closest.Proportions,
            ClosestMahalanobisSquared = closest.MahalanobisSquared,
            ClosestResiduals = closest.Residuals,
            SourceRangeMin = range.Mins,
            SourceRangeMax = range.Maxs,
            MinimumSingularValueRatio = singularData.MinRatio,
            NullSpaceDirections = singularData.NullDirections,
            Events = events,
        };
    }

    private static int DefaultResolution(int k) => k switch
    {
        <= 3 => 60,
        4 => 24,
        _ => 12,
    };

    private static IEnumerable<double[]> SimplexLattice(int k, int r)
    {
        var current = new double[k];
        IEnumerable<double[]> Recurse(int index, int remaining)
        {
            if (index == k - 1)
            {
                current[index] = remaining / (double)r;
                yield return (double[])current.Clone();
                yield break;
            }
            for (var v = 0; v <= remaining; v++)
            {
                current[index] = v / (double)r;
                foreach (var p in Recurse(index + 1, remaining - v))
                    yield return p;
            }
        }
        return Recurse(0, r).ToList();
    }

    private static FeasiblePoint EvaluatePoint(MixingInput input, double[] proportions)
    {
        var k = input.Sources.Count;
        var q = input.Dimensions.Length;
        // Sigma(p) = Sigma_meas + sum_i p_i^2 Sigma_endmember,i : genuine covariance propagation.
        var sigma = LinAlg.Copy(input.MeasurementCovariance);
        for (var i = 0; i < k; i++)
        {
            var cov = input.Sources[i].DistributionCovariance;
            if (cov is null) continue;
            var w = proportions[i] * proportions[i];
            for (var r = 0; r < q; r++)
            for (var c = 0; c < q; c++)
                sigma[r][c] += w * cov[r][c];
        }
        var predicted = new double[q];
        for (var i = 0; i < k; i++)
        for (var r = 0; r < q; r++)
            predicted[r] += proportions[i] * input.EndmemberMatrix[i][r];
        var residuals = LinAlg.Subtract(predicted, input.Sample);
        var d2 = LinAlg.MahalanobisSq(residuals, sigma);
        return new FeasiblePoint(proportions, d2, residuals, sigma, 0);
    }

    private static (double MinRatio, List<double> NullDirections) AnalyzeRank(double[][] matrix)
    {
        var k = matrix.Length;
        var (sv, v) = LinAlg.SvdRight(matrix);
        var max = sv[0];
        var minRatio = max > 0 ? sv[sv.Length - 1] / max : 0.0;
        var nullDirs = new List<double>();
        for (var j = 0; j < sv.Length; j++)
        {
            if (sv[j] / (max + double.Epsilon) < CollinearityRatioThreshold)
            {
                // Columns of V live in endmember-coordinate space only when matrix columns == k.
                if (v.GetLength(0) == k)
                    nullDirs.AddRange(Enumerable.Range(0, k).Select(r => System.Math.Round(v[r][j], 6)));
                break;
            }
        }
        return (minRatio, nullDirs);
    }

    private static IEnumerable<JudgmentEvent> CorrelationEvents(MixingInput input)
    {
        var q = input.Dimensions.Length;
        var sigma = input.MeasurementCovariance;
        var maxOffDiagonal = 0.0;
        for (var r = 0; r < q; r++)
        for (var c = r + 1; c < q; c++)
        {
            var rho = sigma[r][c] / System.Math.Sqrt(sigma[r][r] * sigma[c][c]);
            maxOffDiagonal = System.Math.Max(maxOffDiagonal, System.Math.Abs(rho));
        }
        if (maxOffDiagonal > 1e-9)
        {
            yield return new JudgmentEvent(
                "CORRELATED_ERRORS_USED",
                "info",
                "Measurement errors are correlated; feasibility uses the full covariance matrix, not inflated diagonal variances.",
                new[] { "measurementCovariance" },
                new Dictionary<string, object?>
                {
                    ["maxAbsCorrelation"] = maxOffDiagonal,
                    ["ruleVersion"] = Rules.Version,
                });
        }
        for (var i = 0; i < input.Sources.Count; i++)
        {
            var cov = input.Sources[i].DistributionCovariance;
            if (cov is null) continue;
            for (var r = 0; r < q; r++)
            for (var c = r + 1; c < q; c++)
            {
                if (System.Math.Abs(cov[r][c]) > 1e-12 * System.Math.Sqrt(cov[r][r] * cov[c][c] + 1e-30))
                {
                    yield return new JudgmentEvent(
                        "ENDMEEMBER_COVARIANCE_PROPAGATED",
                        "info",
                        $"Endmember '{input.Sources[i].Label}' distribution covariance (incl. cross terms) is propagated with squared mixing weights.",
                        new[] { input.Sources[i].SourceId },
                        new Dictionary<string, object?> { ["ruleVersion"] = Rules.Version });
                    r = q; c = q;
                }
            }
        }
    }

    private static bool OutsideEnvelope(MixingInput input, out string dimension, out double margin)
    {
        var q = input.Dimensions.Length;
        dimension = input.Dimensions[0];
        margin = 0.0;
        var outside = false;
        for (var r = 0; r < q; r++)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var i = 0; i < input.Sources.Count; i++)
            {
                min = System.Math.Min(min, input.EndmemberMatrix[i][r]);
                max = System.Math.Max(max, input.EndmemberMatrix[i][r]);
            }
            var s = input.Sample[r];
            var localMargin = s < min ? min - s : s > max ? s - max : 0.0;
            if (localMargin > margin)
            {
                margin = localMargin;
                dimension = input.Dimensions[r];
                outside = true;
            }
        }
        return outside;
    }

    private static List<List<double[]>> ClusterComponents(List<FeasiblePoint> points, int resolution)
    {
        var clusters = new List<List<double[]>>();
        foreach (var point in points)
        {
            var p = point.Proportions;
            var found = -1;
            for (var c = 0; c < clusters.Count; c++)
            {
                foreach (var member in clusters[c])
                {
                    var dist = 0.0;
                    for (var i = 0; i < p.Length; i++)
                        dist = System.Math.Max(dist, System.Math.Abs(p[i] - member[i]));
                    if (dist <= RegionMergeTolerance / resolution)
                    {
                        found = c;
                        break;
                    }
                }
                if (found >= 0) break;
            }
            if (found < 0) clusters.Add(new List<double[]> { p });
            else clusters[found].Add(p);
        }
        return clusters;
    }

    private static List<FeasiblePoint> AssignComponentIds(List<FeasiblePoint> points, List<List<double[]>> components)
    {
        var result = new List<FeasiblePoint>();
        foreach (var point in points)
        {
            var id = -1;
            for (var c = 0; c < components.Count; c++)
            {
                foreach (var member in components[c])
                {
                    var same = true;
                    for (var i = 0; i < member.Length; i++)
                        if (System.Math.Abs(member[i] - point.Proportions[i]) > 1e-12) { same = false; break; }
                    if (same) { id = c; break; }
                }
                if (id >= 0) break;
            }
            result.Add(point with { ComponentId = id });
        }
        return result;
    }

    private static List<FeasibleRegion> BuildRegions(MixingInput input, List<List<double[]>> components, int resolution)
    {
        var regions = new List<FeasibleRegion>();
        for (var c = 0; c < components.Count; c++)
        {
            var pts = components[c];
            var k = input.Sources.Count;
            var centroid = new double[k];
            var mins = Enumerable.Repeat(double.PositiveInfinity, k).ToArray();
            var maxs = Enumerable.Repeat(double.NegativeInfinity, k).ToArray();
            double[] best = pts[0];
            var bestD = double.PositiveInfinity;
            foreach (var p in pts)
            {
                for (var i = 0; i < k; i++)
                {
                    centroid[i] += p[i] / pts.Count;
                    mins[i] = System.Math.Min(mins[i], p[i]);
                    maxs[i] = System.Math.Max(maxs[i], p[i]);
                }
                var eval = EvaluatePoint(input, p);
                if (eval.MahalanobisSquared < bestD) { bestD = eval.MahalanobisSquared; best = p; }
            }
            regions.Add(new FeasibleRegion(
                c,
                centroid,
                best,
                mins,
                maxs,
                ConvexHull2D(pts),
                pts.Count));
        }
        return regions;
    }

    private static List<double[]> ConvexHull2D(List<double[]> pts)
    {
        if (pts[0].Length > 3) return pts.Take(1).ToList();
        // For k <= 3 the simplex is intrinsically planar; use (p0, p1) coordinates.
        var projected = pts.Select(p => (x: p[0], y: p.Length > 1 ? p[1] : 0.0, p)).ToList();
        projected.Sort((a, b) => a.x.CompareTo(b.x) != 0 ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        if (projected.Count <= 2) return projected.Select(t => t.p).ToList();
        List<(double x, double y, double[] p)> Build(int sign)
        {
            var hull = new List<(double, double, double[])>();
            foreach (var t in projected)
            {
                while (hull.Count >= 2)
                {
                    var a = hull[^2];
                    var b = hull[^1];
                    var cross = (b.Item1 - a.Item1) * (t.y - a.Item2) - (b.Item2 - a.Item2) * (t.x - a.Item1);
                    if (sign * cross <= 0) hull.RemoveAt(hull.Count - 1);
                    else break;
                }
                hull.Add(t);
            }
            return hull;
        }
        var lower = Build(1);
        var upper = Build(-1);
        return lower.Concat(upper.Skip(1).SkipLast(1)).Select(t => t.Item3).ToList();
    }

    private static (double[] Proportions, double MahalanobisSquared, double[] Residuals) ClosestPoint(
        MixingInput input, double[] lo, double[] up)
    {
        var k = input.Sources.Count;
        var p = Enumerable.Repeat(1.0 / k, k).ToArray();
        var step = 0.2;
        for (var iter = 0; iter < 400; iter++)
        {
            var eval = EvaluatePoint(input, p);
            // Numerical gradient of d(p)^2 with propagated covariance (finite differences).
            var grad = new double[k];
            const double eps = 1e-5;
            for (var i = 0; i < k; i++)
            {
                var pushed = (double[])p.Clone();
                pushed[i] += eps;
                var pulled = (double[])p.Clone();
                pulled[i] -= eps;
                var evalPushed = EvaluatePoint(input, Simplex.Project(pushed, lo, up));
                var evalPulled = EvaluatePoint(input, Simplex.Project(pulled, lo, up));
                grad[i] = (evalPushed.MahalanobisSquared - evalPulled.MahalanobisSquared) /
                          (evalPushed.Proportions[i] - evalPulled.Proportions[i] + 1e-12);
            }
            var candidate = new double[k];
            for (var i = 0; i < k; i++) candidate[i] = p[i] - step * grad[i];
            var projected = Simplex.Project(candidate, lo, up);
            var nextEval = EvaluatePoint(input, projected);
            if (nextEval.MahalanobisSquared < eval.MahalanobisSquared - 1e-13)
            {
                p = projected;
            }
            else
            {
                step *= 0.5;
                if (step < 1e-9) break;
            }
        }
        var finalEval = EvaluatePoint(input, p);
        return (p, finalEval.MahalanobisSquared, finalEval.Residuals);
    }

    private static (double[] Mins, double[] Maxs, double MinMin, double MaxMax) SourceRanges(List<FeasiblePoint> points)
    {
        if (points.Count == 0)
        {
            var k0 = 0;
            return (System.Array.Empty<double>(), System.Array.Empty<double>(), 0, 0);
        }
        var k = points[0].Proportions.Length;
        var mins = Enumerable.Repeat(double.PositiveInfinity, k).ToArray();
        var maxs = Enumerable.Repeat(double.NegativeInfinity, k).ToArray();
        foreach (var point in points)
        {
            for (var i = 0; i < k; i++)
            {
                mins[i] = System.Math.Min(mins[i], point.Proportions[i]);
                maxs[i] = System.Math.Max(maxs[i], point.Proportions[i]);
            }
        }
        return (mins, maxs, mins.Min(), maxs.Max());
    }
}

/// <summary>Rule version tag. Bumped only when derivation rules genuinely change.</summary>
public static class Rules
{
    public const string Version = "mixing-rules-1.0.0";
}

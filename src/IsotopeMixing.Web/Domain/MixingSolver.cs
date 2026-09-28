using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsotopeMixing.Web.Domain;

public static class MixingSolver
{
    private const double Tolerance = 1E-8;

    public static SampleAnalysis Analyze(SampleRecord sample, EndmemberVersion endmembers, FractionationVersion fractionation, IReadOnlyList<IsotopeDimension> dimensions)
    {
        var present = dimensions.Where(dimension => sample.Observed.TryGetValue(dimension.Id, out var value) && value.Value.HasValue).ToList();
        var missing = dimensions.Where(dimension => !present.Contains(dimension)).Select(dimension => dimension.Id).ToList();
        var k = endmembers.Endmembers.Count;
        var rows = new List<double[]>();
        var targets = new List<double>();
        foreach (var dimension in present)
        {
            var observed = sample.Observed[dimension.Id].Value!.Value;
            targets.Add(observed);
            var row = new double[k];
            for (var source = 0; source < k; source++)
            {
                var mean = endmembers.Endmembers[source].Mean[dimension.Id].Value!.Value;
                var epsilon = fractionation.EpsilonByEndmember
                    .GetValueOrDefault(endmembers.Endmembers[source].Id, new Dictionary<string, RawValue>())
                    .GetValueOrDefault(dimension.Id)?.Value ?? 0d;
                row[source] = mean + epsilon;
            }
            rows.Add(row);
        }

        var matrix = rows.ToArray();
        var y = targets.ToArray();
        var covariance = ExtractCovariance(sample, present);
        var correlated = HasCorrelatedError(covariance);
        var chiSquaredThreshold = ChiSquared95(present.Count);
        var f0 = new double[k - 1].Concat(new[] { 1d }).ToArray();
        var nullBasis = EqualityNullBasis(k);
        var effective = Multiply(matrix, nullBasis);
        var centeredTarget = Subtract(y, Multiply(matrix, f0));
        var svd = Svd.Decompose(effective);
        var affineRank = present.Count == 0 ? 1 : EffectiveRank(svd, effective);
        var nearCollinear = present.Count >= 2 && k == 3 && Math.Abs(Determinant3(AugmentAffine(matrix))) < 1E-5;
        var underidentified = affineRank < k - 1;

        var simplexComponent = new PriorComponent("simplex", Enumerable.Range(0, k).Select(i => new FractionBound(0, 1)).ToArray());
        var components = new List<ComponentAnalysis>();
        foreach (var prior in sample.Prior.Components.Prepend(simplexComponent))
        {
            components.Add(AnalyzeComponent(prior, matrix, y, covariance, f0, nullBasis, effective, centeredTarget, svd, affineRank, chiSquaredThreshold, k));
        }

        var envelope = components[0];
        var priorComponents = components.Skip(1).ToList();
        var deterministicPriorFeasible = priorComponents.Any(component => component.DeterministicFeasible);
        var confidenceRegions = priorComponents.Where(component => component.ConfidencePoints.Count > 0).ToList();
        var bestFit = envelope.BestFit;
        var residual = bestFit is null ? null : Residual(matrix, y, covariance, bestFit);

        return new SampleAnalysis(
            sample.Id,
            endmembers.Id,
            fractionation.Id,
            present.Select(dimension => dimension.Id).ToArray(),
            missing.ToArray(),
            affineRank,
            nearCollinear,
            underidentified,
            correlated,
            !envelope.DeterministicFeasible,
            !envelope.DeterministicFeasible || deterministicPriorFeasible ? deterministicPriorFeasible : false,
            confidenceRegions.Count > 1,
            chiSquaredThreshold,
            residual,
            bestFit,
            components,
            BuildDiagnostics(present.Count == 0, nearCollinear, underidentified, correlated, missing, !envelope.DeterministicFeasible, envelope.DeterministicFeasible && !deterministicPriorFeasible, confidenceRegions.Count > 1));
    }

    private static double[][] ExtractCovariance(SampleRecord sample, IReadOnlyList<IsotopeDimension> present)
    {
        var n = present.Count;
        var covariance = new double[n][];
        for (var row = 0; row < n; row++)
        {
            covariance[row] = new double[n];
            for (var column = 0; column < n; column++)
            {
                covariance[row][column] = sample.Covariance[row][column].Value ?? (row == column ? 1 : 0);
            }
        }
        return covariance;
    }

    private static bool HasCorrelatedError(double[][] covariance)
    {
        for (var row = 0; row < covariance.Length; row++)
        for (var column = row + 1; column < covariance.Length; column++)
            if (Math.Abs(covariance[row][column]) > Tolerance)
                return true;
        return false;
    }

    private static double ChiSquared95(int degrees) => degrees switch
    {
        0 => 0,
        1 => 3.841458820694124,
        2 => 5.991464547107979,
        3 => 7.814727903251178,
        _ => degrees * Math.Pow(1 - 2.0 / (9 * degrees) + 1.6448536269514722 * Math.Sqrt(2.0 / (9 * degrees)), 3)
    };

    private static double[][] EqualityNullBasis(int k)
    {
        var basis = new double[k][];
        for (var row = 0; row < k; row++)
        {
            basis[row] = new double[k - 1];
            for (var column = 0; column < k - 1; column++)
                basis[row][column] = row == column ? 1 : row == k - 1 ? -1 : 0;
        }
        return basis;
    }

    private static double[][] AugmentAffine(double[][] matrix)
    {
        var d = matrix.Length;
        var k = matrix[0].Length;
        var augmented = new double[d + 1][];
        for (var row = 0; row < d; row++) augmented[row] = matrix[row];
        augmented[d] = Enumerable.Repeat(1d, k).ToArray();
        return augmented;
    }

    private static int EffectiveRank(SvdResult svd, double[][] matrix)
    {
        var rows = matrix.Length;
        var columns = matrix[0].Length;
        var tolerance = Math.Max(rows, columns) * svd.W[0] * 1E-10;
        return svd.W.Count(value => value > tolerance);
    }

    private static ComponentAnalysis AnalyzeComponent(
        PriorComponent prior,
        double[][] matrix,
        double[] y,
        double[][] covariance,
        double[] f0,
        double[][] nullBasis,
        double[][] effective,
        double[] centeredTarget,
        SvdResult svd,
        int affineRank,
        double threshold,
        int k)
    {
        var polygon = SimplexPolygon(k);
        foreach (var bound in prior.Bounds.Select((bound, index) => (bound, index)))
        {
            polygon = ClipPolygon(polygon, nullBasis, f0, bound.index, bound.bound.Minimum, true);
            polygon = ClipPolygon(polygon, nullBasis, f0, bound.index, bound.bound.Maximum, false);
        }

        double[]? bestFit = null;
        var deterministic = polygon.Count > 0;
        if (polygon.Count > 0 && matrix.Length > 0)
        {
            var optimum = QuadraticMinimizer.Minimize(matrix, y, covariance, nullBasis, f0, polygon);
            bestFit = optimum.Fractions;
            deterministic = optimum.ResidualMahalanobis < 1E-5;
        }

        var confidence = new List<double[]>();
        var confidenceSegments = new List<FractionSegment>();
        var centerResidual = double.PositiveInfinity;
        if (matrix.Length > 0)
        {
            if (affineRank == nullBasis[0].Length)
            {
                var ellipse = EllipseRegion.Build(matrix, y, covariance, nullBasis, f0, threshold);
                var sampled = ellipse.Polygon;
                foreach (var bound in prior.Bounds.Select((bound, index) => (bound, index)))
                {
                    sampled = ClipPolygon(sampled, nullBasis, f0, bound.index, bound.bound.Minimum, true);
                    sampled = ClipPolygon(sampled, nullBasis, f0, bound.index, bound.bound.Maximum, false);
                }
                confidence = sampled;
                centerResidual = ellipse.CenterMahalanobis;
            }
            else if (affineRank == 1)
            {
                var line = LineRegion.Build(matrix, y, covariance, nullBasis, f0, svd, threshold, prior, k);
                confidenceSegments.Add(line.Segment);
                centerResidual = line.CenterMahalanobis;
            }
        }

        return new ComponentAnalysis(
            prior.Id,
            prior.Bounds.Select(bound => new FractionRange(bound.Minimum, bound.Maximum)).ToArray(),
            deterministic,
            polygon,
            confidence,
            confidenceSegments,
            bestFit,
            bestFit is null ? null : Residual(matrix, y, covariance, bestFit),
            centerResidual);
    }

    private static List<double[]> SimplexPolygon(int k)
    {
        if (k != 3) return new List<double[]>();
        return new List<double[]> { new[] { 1d, 0, 0 }, new[] { 0d, 1, 0 }, new[] { 0d, 0, 1 } };
    }

    private static List<double[]> ClipPolygon(List<double[]> polygon, double[][] basis, double[] f0, int fractionIndex, double limit, bool minimum)
    {
        if (polygon.Count == 0) return polygon;
        var result = new List<double[]>();
        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var previous = polygon[(i - 1 + polygon.Count) % polygon.Count];
            var currentValue = current[fractionIndex] - limit;
            var previousValue = previous[fractionIndex] - limit;
            var currentInside = minimum ? currentValue >= -Tolerance : currentValue <= Tolerance;
            var previousInside = minimum ? previousValue >= -Tolerance : previousValue <= Tolerance;
            if (currentInside)
            {
                if (!previousInside) result.Add(Intersect(previous, current, fractionIndex, limit));
                result.Add(current);
            }
            else if (previousInside)
            {
                result.Add(Intersect(previous, current, fractionIndex, limit));
            }
        }
        return result;
    }

    private static double[] Intersect(double[] first, double[] second, int index, double limit)
    {
        var denominator = second[index] - first[index];
        var t = Math.Abs(denominator) < Tolerance ? 0 : (limit - first[index]) / denominator;
        return first.Select((value, i) => value + (second[i] - value) * t).ToArray();
    }

    private static double[][] Multiply(double[][] left, double[][] right)
    {
        var result = new double[left.Length][];
        for (var row = 0; row < left.Length; row++)
        {
            result[row] = new double[right[0].Length];
            for (var column = 0; column < right[0].Length; column++)
                for (var inner = 0; inner < right.Length; inner++)
                    result[row][column] += left[row][inner] * right[inner][column];
        }
        return result;
    }

    private static double[] Multiply(double[][] matrix, double[] vector)
    {
        var result = new double[matrix.Length];
        for (var row = 0; row < matrix.Length; row++)
            for (var column = 0; column < vector.Length; column++)
                result[row] += matrix[row][column] * vector[column];
        return result;
    }

    private static double[] Subtract(double[] left, double[] right) => left.Zip(right).Select(pair => pair.First - pair.Second).ToArray();

    private static double Determinant3(double[][] matrix)
    {
        if (matrix.Length != 3 || matrix[0].Length != 3) return 0;
        return matrix[0][0] * (matrix[1][1] * matrix[2][2] - matrix[1][2] * matrix[2][1])
             - matrix[0][1] * (matrix[1][0] * matrix[2][2] - matrix[1][2] * matrix[2][0])
             + matrix[0][2] * (matrix[1][0] * matrix[2][1] - matrix[1][1] * matrix[2][0]);
    }

    private static ResidualSummary Residual(double[][] matrix, double[] y, double[][] covariance, double[] fractions)
    {
        var predicted = Multiply(matrix, fractions);
        var raw = Subtract(y, predicted);
        var inverse = LinearAlgebra.Inverse(covariance);
        var mahalanobis = Quadratic(raw, inverse);
        return new ResidualSummary(raw, predicted, Math.Sqrt(raw.Select(value => value * value).Sum() / raw.Length), mahalanobis);
    }

    private static double Quadratic(double[] vector, double[][] matrix)
    {
        var total = 0d;
        for (var row = 0; row < vector.Length; row++)
        for (var column = 0; column < vector.Length; column++)
            total += vector[row] * matrix[row][column] * vector[column];
        return total;
    }

    private static IReadOnlyList<string> BuildDiagnostics(bool noDimensions, bool nearCollinear, bool underidentified, bool correlated, IReadOnlyList<string> missing, bool outside, bool priorInfeasible, bool multipleRegions)
    {
        var diagnostics = new List<string>();
        if (noDimensions || missing.Count > 0) diagnostics.Add($"missing-isotope-dimension:{string.Join(",", missing)}");
        if (nearCollinear) diagnostics.Add("near-collinear-endmembers:affine-rank-insufficient");
        if (underidentified) diagnostics.Add("underidentified:feasible-set-retained");
        if (correlated) diagnostics.Add("correlated-measurement-error:covariance-propagated");
        if (outside) diagnostics.Add("sample-outside-endmember-envelope");
        if (priorInfeasible) diagnostics.Add("infeasible-prior:no-truncation-or-renormalization");
        if (multipleRegions) diagnostics.Add("multiple-feasible-regions:all-components-retained");
        return diagnostics;
    }
}

public sealed record SampleAnalysis(
    string SampleId,
    string EndmemberVersionId,
    string FractionationVersionId,
    IReadOnlyList<string> ObservedDimensionIds,
    IReadOnlyList<string> MissingDimensionIds,
    int AffineRank,
    bool NearCollinear,
    bool Underidentified,
    bool CorrelatedMeasurementError,
    bool OutsideEnvelope,
    bool PriorInfeasible,
    bool MultipleFeasibleRegions,
    double ConfidenceLevelChiSquared,
    ResidualSummary? BestResidual,
    IReadOnlyList<double>? BestFit,
    IReadOnlyList<ComponentAnalysis> Components,
    IReadOnlyList<string> Diagnostics);

public sealed record ComponentAnalysis(
    string PriorId,
    IReadOnlyList<FractionRange> FractionBounds,
    bool DeterministicFeasible,
    IReadOnlyList<double[]> EnvelopeRegion,
    IReadOnlyList<double[]> ConfidencePoints,
    IReadOnlyList<FractionSegment> ConfidenceSegments,
    IReadOnlyList<double>? BestFit,
    ResidualSummary? BestResidual,
    double CenterMahalanobis);

public sealed record FractionRange(double Minimum, double Maximum);

public sealed record FractionSegment(double[] Start, double[] End);

public sealed record ResidualSummary(IReadOnlyList<double> Raw, IReadOnlyList<double> Predicted, double Rms, double Mahalanobis);

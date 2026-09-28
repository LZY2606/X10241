namespace IsotopeMixing.Web.Domain;

public static class LineRegion
{
    public static LineResult Build(double[][] matrix, double[] y, double[][] covariance, double[][] nullBasis, double[] f0, SvdResult svd, double chiSquared, PriorComponent prior, int k)
    {
        var direction = svd.V[1];
        var rankDirection = svd.V[0];
        var effective = Multiply(matrix, nullBasis);
        var projectedTarget = Subtract(y, Multiply(matrix, f0));
        var a = Dot(projectedTarget, Multiply(effective, rankDirection));
        var b = Dot(Multiply(effective, rankDirection), Multiply(effective, rankDirection));
        var hReduced = rankDirection.Select(value => value * (b < 1E-15 ? 0 : a / b)).ToArray();
        var center = Combine(nullBasis, f0, hReduced);
        var residual = Subtract(projectedTarget, Multiply(effective, hReduced));
        var inverseCovariance = LinearAlgebra.Inverse(covariance);
        var centerMahalanobis = 0d;
        for (var row = 0; row < residual.Length; row++)
        for (var column = 0; column < residual.Length; column++)
            centerMahalanobis += residual[row] * inverseCovariance[row][column] * residual[column];
        var slack = Math.Sqrt(Math.Max(0, chiSquared - centerMahalanobis));
        var radius = slack / Math.Max(1E-15, Math.Sqrt(Math.Max(0, Dot(Multiply(effective, direction), Multiply(effective, direction)))));
        var startReduced = hReduced.Select((value, index) => value - (radius * direction[index])).ToArray();
        var endReduced = hReduced.Select((value, index) => value + (radius * direction[index])).ToArray();
        var start = Combine(nullBasis, f0, startReduced);
        var end = Combine(nullBasis, f0, endReduced);
        foreach (var bound in prior.Bounds.Select((bound, index) => (bound, index)))
        {
            Clamp(ref start, ref end, bound.index, bound.bound.Minimum, true);
            Clamp(ref start, ref end, bound.index, bound.bound.Maximum, false);
        }
        return new LineResult(new FractionSegment(start, end), center, centerMahalanobis);
    }

    private static void Clamp(ref double[] start, ref double[] end, int index, double limit, bool minimum)
    {
        var low = Math.Min(start[index], end[index]);
        var high = Math.Max(start[index], end[index]);
        if (minimum && high < limit - 1E-8 || !minimum && low > limit + 1E-8)
        {
            start = start.Select(value => double.NaN).ToArray();
            end = end.Select(value => double.NaN).ToArray();
            return;
        }
        var tMin = (limit - start[index]) / (end[index] - start[index]);
        var tMax = tMin;
        if (minimum) start = Interpolate(start, end, Math.Max(0, tMin));
        else end = Interpolate(start, end, Math.Min(1, tMax));
    }

    private static double[] Interpolate(double[] start, double[] end, double t) => start.Zip(end).Select(pair => pair.First + ((pair.Second - pair.First) * t)).ToArray();

    private static double[][] Multiply(double[][] left, double[][] right)
    {
        var result = new double[left.Length][];
        for (var row = 0; row < left.Length; row++)
        {
            result[row] = new double[right[0].Length];
            for (var column = 0; column < right[0].Length; column++)
                for (var inner = 0; inner < right.Length; inner++) result[row][column] += left[row][inner] * right[inner][column];
        }
        return result;
    }

    private static double[] Multiply(double[][] matrix, double[] vector) => matrix.Select(row => row.Zip(vector).Sum(pair => pair.First * pair.Second)).ToArray();
    private static double[] Subtract(double[] left, double[] right) => left.Zip(right).Select(pair => pair.First - pair.Second).ToArray();
    private static double[] Combine(double[][] basis, double[] f0, double[] x)
    {
        var result = f0.ToArray();
        for (var row = 0; row < basis.Length; row++)
        for (var column = 0; column < x.Length; column++) result[row] += basis[row][column] * x[column];
        return result;
    }

    private static double Dot(double[] left, double[] right) => left.Zip(right).Sum(pair => pair.First * pair.Second);
}

public sealed record LineResult(FractionSegment Segment, double[] Center, double CenterMahalanobis);

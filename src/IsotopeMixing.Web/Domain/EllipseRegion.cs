namespace IsotopeMixing.Web.Domain;

public static class EllipseRegion
{
    public static EllipseResult Build(double[][] matrix, double[] y, double[][] covariance, double[][] nullBasis, double[] f0, double chiSquared)
    {
        var inverseCovariance = LinearAlgebra.Inverse(covariance);
        var effective = Multiply(matrix, nullBasis);
        var target = Subtract(y, Multiply(matrix, f0));
        var m = nullBasis[0].Length;
        var q = new double[m][];
        var linear = new double[m];
        for (var i = 0; i < m; i++)
        {
            q[i] = new double[m];
            for (var j = 0; j < m; j++)
            for (var row = 0; row < effective.Length; row++)
            for (var column = 0; column < effective.Length; column++)
                q[i][j] += effective[row][i] * inverseCovariance[row][column] * effective[column][j];
            for (var row = 0; row < effective.Length; row++)
            for (var column = 0; column < effective.Length; column++)
                linear[i] -= 2 * effective[row][i] * inverseCovariance[row][column] * target[column];
        }

        var h = LinearAlgebra.SolveSymmetric(q, linear.Select(value => -value / 2).ToArray());
        var shifted = Subtract(target, Multiply(effective, h));
        var centerMahalanobis = 0d;
        for (var row = 0; row < effective.Length; row++)
        for (var column = 0; column < effective.Length; column++)
            centerMahalanobis += shifted[row] * inverseCovariance[row][column] * shifted[column];
        var radiusScale = Math.Sqrt(Math.Max(0, chiSquared - centerMahalanobis));
        var eigen = LinearAlgebra.Eigen2Symmetric(q);
        var radii = new[]
        {
            radiusScale / Math.Sqrt(Math.Max(1E-18, eigen.FirstEigenvalue)),
            radiusScale / Math.Sqrt(Math.Max(1E-18, eigen.SecondEigenvalue))
        };

        var polygon = new List<double[]>();
        for (var angleIndex = 0; angleIndex < 96; angleIndex++)
        {
            var angle = 2 * Math.PI * angleIndex / 96d;
            var x = h[0] + (radii[0] * Math.Cos(angle) * eigen.FirstVector[0]) + (radii[1] * Math.Sin(angle) * eigen.SecondVector[0]);
            var z = h[1] + (radii[0] * Math.Cos(angle) * eigen.FirstVector[1]) + (radii[1] * Math.Sin(angle) * eigen.SecondVector[1]);
            polygon.Add(Combine(nullBasis, f0, new[] { x, z }));
        }
        return new EllipseResult(polygon, Combine(nullBasis, f0, h), centerMahalanobis);
    }

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
        for (var column = 0; column < x.Length; column++)
            result[row] += basis[row][column] * x[column];
        return result;
    }
}

public sealed record EllipseResult(IReadOnlyList<double[]> Polygon, double[] Center, double CenterMahalanobis);

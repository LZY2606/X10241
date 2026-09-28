using System.Globalization;

namespace IsotopeMixing.Web.Domain;

public static class LinearAlgebra
{
    public static double[][] Inverse(double[][] matrix)
    {
        var n = matrix.Length;
        var augmented = new double[n][];
        for (var i = 0; i < n; i++)
        {
            augmented[i] = new double[2 * n];
            Array.Copy(matrix[i], augmented[i], n);
            augmented[i][n + i] = 1;
        }

        for (var pivot = 0; pivot < n; pivot++)
        {
            var row = pivot;
            for (var candidate = pivot + 1; candidate < n; candidate++)
                if (Math.Abs(augmented[candidate][pivot]) > Math.Abs(augmented[row][pivot])) row = candidate;
            (augmented[pivot], augmented[row]) = (augmented[row], augmented[pivot]);
            var divisor = augmented[pivot][pivot];
            if (Math.Abs(divisor) < 1E-15) throw new InvalidOperationException("Singular covariance is not allowed for feasible error propagation.");
            for (var column = 0; column < 2 * n; column++) augmented[pivot][column] /= divisor;
            for (var other = 0; other < n; other++)
            {
                if (other == pivot) continue;
                var factor = augmented[other][pivot];
                for (var column = 0; column < 2 * n; column++) augmented[other][column] -= factor * augmented[pivot][column];
            }
        }

        return augmented.Select(row => row.Skip(n).ToArray()).ToArray();
    }

    public static double[] SolveSymmetric(double[][] matrix, double[] vector)
    {
        var n = vector.Length;
        var a = matrix.Select(row => row.ToArray()).ToArray();
        var b = vector.ToArray();
        for (var pivot = 0; pivot < n; pivot++)
        {
            var row = pivot;
            for (var candidate = pivot + 1; candidate < n; candidate++)
                if (Math.Abs(a[candidate][pivot]) > Math.Abs(a[row][pivot])) row = candidate;
            (a[pivot], a[row]) = (a[row], a[pivot]);
            (b[pivot], b[row]) = (b[row], b[pivot]);
            if (Math.Abs(a[pivot][pivot]) < 1E-15) throw new InvalidOperationException("Singular quadratic system.");
            for (var other = pivot + 1; other < n; other++)
            {
                var factor = a[other][pivot] / a[pivot][pivot];
                b[other] -= factor * b[pivot];
                for (var column = pivot; column < n; column++) a[other][column] -= factor * a[pivot][column];
            }
        }

        var result = new double[n];
        for (var row = n - 1; row >= 0; row--)
        {
            result[row] = b[row];
            for (var column = row + 1; column < n; column++) result[row] -= a[row][column] * result[column];
            result[row] /= a[row][row];
        }
        return result;
    }

    public static double Determinant(double[][] matrix)
    {
        var n = matrix.Length;
        var copy = matrix.Select(row => row.ToArray()).ToArray();
        var determinant = 1d;
        for (var pivot = 0; pivot < n; pivot++)
        {
            var row = pivot;
            for (var candidate = pivot + 1; candidate < n; candidate++)
                if (Math.Abs(copy[candidate][pivot]) > Math.Abs(copy[row][pivot])) row = candidate;
            if (row != pivot)
            {
                (copy[pivot], copy[row]) = (copy[row], copy[pivot]);
                determinant *= -1;
            }
            if (Math.Abs(copy[pivot][pivot]) < 1E-15) return 0;
            determinant *= copy[pivot][pivot];
            for (var other = pivot + 1; other < n; other++)
            {
                var factor = copy[other][pivot] / copy[pivot][pivot];
                for (var column = pivot; column < n; column++) copy[other][column] -= factor * copy[pivot][column];
            }
        }
        return determinant;
    }

    public static Eigen2 Eigen2Symmetric(double[][] matrix)
    {
        var a = matrix[0][0];
        var b = matrix[0][1];
        var d = matrix[1][1];
        var trace = a + d;
        var discriminant = Math.Sqrt(((a - d) * (a - d)) + (4 * b * b));
        var first = (trace + discriminant) / 2;
        var second = (trace - discriminant) / 2;
        var vector = Math.Abs(b) > 1E-15 ? new[] { first - d, b } : new[] { 1d, 0 };
        var length = Math.Sqrt((vector[0] * vector[0]) + (vector[1] * vector[1]));
        if (length < 1E-15) vector = new[] { 1d, 0 };
        else vector = vector.Select(value => value / length).ToArray();
        if (second > first)
        {
            (first, second) = (second, first);
            vector = new[] { -vector[1], vector[0] };
        }
        return new Eigen2(first, second, vector, new[] { -vector[1], vector[0] });
    }

    public static string Format(double value) => value.ToString("G12", CultureInfo.InvariantCulture);
}

public sealed record Eigen2(double FirstEigenvalue, double SecondEigenvalue, double[] FirstVector, double[] SecondVector);

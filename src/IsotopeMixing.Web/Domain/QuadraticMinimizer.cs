namespace IsotopeMixing.Web.Domain;

public static class QuadraticMinimizer
{
    public static QuadraticMinimum Minimize(double[][] matrix, double[] y, double[][] covariance, double[][] nullBasis, double[] f0, IReadOnlyList<double[]> polygon)
    {
        var inverseCovariance = LinearAlgebra.Inverse(covariance);
        var effective = Multiply(matrix, nullBasis);
        var target = Subtract(y, Multiply(matrix, f0));
        var m = nullBasis[0].Length;
        var q = new double[m][];
        for (var i = 0; i < m; i++)
        {
            q[i] = new double[m];
            for (var j = 0; j < m; j++)
            for (var row = 0; row < effective.Length; row++)
            for (var column = 0; column < effective.Length; column++)
                q[i][j] += effective[row][i] * inverseCovariance[row][column] * effective[column][j];
        }

        var linear = new double[m];
        for (var i = 0; i < m; i++)
        for (var row = 0; row < effective.Length; row++)
        for (var column = 0; column < effective.Length; column++)
            linear[i] -= 2 * effective[row][i] * inverseCovariance[row][column] * target[column];

        var bestX = Enumerable.Repeat(0d, m).ToArray();
        var bestCost = double.PositiveInfinity;
        var vertices = polygon.Count > 0 ? polygon : new List<double[]>();
        foreach (var point in vertices)
        {
            var x = ToReduced(point, nullBasis, f0);
            var cost = Cost(q, linear, x);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestX = x;
            }
        }

        var bounds = Enumerable.Range(0, m).Select(index => (polygon.Min(point => ToReduced(point, nullBasis, f0)[index]), polygon.Max(point => ToReduced(point, nullBasis, f0)[index]))).ToArray();
        foreach (var mask in Enumerable.Range(0, 1 << m))
        {
            var fixedCoordinates = new List<(int Index, double Value)>();
            for (var i = 0; i < m; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                foreach (var value in new[] { bounds[i].Item1, bounds[i].Item2 })
                {
                    var candidate = fixedCoordinates.ToList();
                    candidate.Add((i, value));
                    TryStationary(q, linear, candidate, polygon, nullBasis, f0, ref bestCost, ref bestX);
                }
            }
            TryStationary(q, linear, fixedCoordinates, polygon, nullBasis, f0, ref bestCost, ref bestX);
        }

        var fractions = nullBasis.Zip(bestX).Select(pair => f0[pair.First[0] == 0 && pair.First.Skip(1).All(v => v == 0) ? 0 : Array.IndexOf(pair.First, 1)]).ToArray();
        fractions = Combine(nullBasis, f0, bestX);
        var residual = RawResidual(matrix, y, covariance, fractions);
        return new QuadraticMinimum(fractions, residual);
    }

    private static void TryStationary(double[][] q, double[] linear, IReadOnlyList<(int Index, double Value)> fixedCoordinates, IReadOnlyList<double[]> polygon, double[][] basis, double[] f0, ref double bestCost, ref double[] bestX)
    {
        var m = linear.Length;
        var free = Enumerable.Range(0, m).Where(index => fixedCoordinates.All(point => point.Index != index)).ToArray();
        if (free.Length == 0)
        {
            var candidateX = new double[m];
            foreach (var point in fixedCoordinates) candidateX[point.Index] = point.Value;
            Accept(q, linear, candidateX, polygon, basis, f0, ref bestCost, ref bestX);
            return;
        }

        var reducedQ = free.Select(i => free.Select(j => q[i][j]).ToArray()).ToArray();
        var reducedLinear = free.Select(i =>
        {
            var value = linear[i];
            foreach (var fixedPoint in fixedCoordinates) value += q[i][fixedPoint.Index] * fixedPoint.Value;
            return value;
        }).ToArray();
        var stationarity = reducedLinear.Select(value => -value / 2).ToArray();
        var reducedX = LinearAlgebra.SolveSymmetric(reducedQ, stationarity);
        var candidate = new double[m];
        foreach (var point in fixedCoordinates) candidate[point.Index] = point.Value;
        foreach (var pair in free.Zip(reducedX)) candidate[pair.First] = pair.Second;
        Accept(q, linear, candidate, polygon, basis, f0, ref bestCost, ref bestX);
    }

    private static void Accept(double[][] q, double[] linear, double[] x, IReadOnlyList<double[]> polygon, double[][] basis, double[] f0, ref double bestCost, ref double[] bestX)
    {
        var fractions = Combine(basis, f0, x);
        if (fractions.Any(value => value < -1E-8 || value > 1 + 1E-8)) return;
        if (Math.Abs(fractions.Sum() - 1) > 1E-8) return;
        var cost = Cost(q, linear, x);
        if (cost < bestCost)
        {
            bestCost = cost;
            bestX = x.ToArray();
        }
    }

    private static double Cost(double[][] q, double[] linear, double[] x)
    {
        var total = 0d;
        for (var i = 0; i < x.Length; i++)
        {
            total += q[i][i] * x[i] * x[i];
            total += linear[i] * x[i];
            for (var j = i + 1; j < x.Length; j++) total += 2 * q[i][j] * x[i] * x[j];
        }
        return total;
    }

    private static double[] ToReduced(double[] fraction, double[][] basis, double[] f0) => Enumerable.Range(0, basis[0].Length).Select(i => fraction[i] - fraction[^1]).ToArray();

    private static double[] Combine(double[][] basis, double[] f0, double[] x)
    {
        var result = f0.ToArray();
        for (var row = 0; row < basis.Length; row++)
        for (var column = 0; column < x.Length; column++)
                result[row] += basis[row][column] * x[column];
        return result;
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

    private static double RawResidual(double[][] matrix, double[] y, double[][] covariance, double[] fractions)
    {
        var residual = Subtract(y, Multiply(matrix, fractions));
        var inverse = LinearAlgebra.Inverse(covariance);
        var total = 0d;
        for (var i = 0; i < residual.Length; i++)
        for (var j = 0; j < residual.Length; j++) total += residual[i] * inverse[i][j] * residual[j];
        return total;
    }
}

public sealed record QuadraticMinimum(double[] Fractions, double ResidualMahalanobis);

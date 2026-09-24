namespace StableIsotopeMixing.Web.Domain;

public static class LinAlg
{
    public static double[,] Inverse(double[,] matrix)
    {
        var n = matrix.GetLength(0);
        var a = new double[n, 2 * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                a[i, j] = matrix[i, j];
            }
            a[i, n + i] = 1.0;
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(a[pivot, col]) < 1e-12)
            {
                a[pivot, col] = 1e-12;
            }

            if (pivot != col)
            {
                for (var j = 0; j < 2 * n; j++)
                {
                    (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);
                }
            }

            var divisor = a[col, col];
            for (var j = 0; j < 2 * n; j++)
            {
                a[col, j] /= divisor;
            }

            for (var row = 0; row < n; row++)
            {
                if (row == col)
                {
                    continue;
                }
                var factor = a[row, col];
                for (var j = 0; j < 2 * n; j++)
                {
                    a[row, j] -= factor * a[col, j];
                }
            }
        }

        var inverse = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                inverse[i, j] = a[i, n + j];
            }
        }
        return inverse;
    }

    public static double[] EigenvaluesSymmetric(double[,] matrix)
    {
        var n = matrix.GetLength(0);
        var a = (double[,])matrix.Clone();
        for (var sweep = 0; sweep < 200; sweep++)
        {
            var p = 0;
            var q = n > 1 ? 1 : 0;
            var max = 0.0;
            for (var i = 0; i < n; i++)
            {
                for (var j = i + 1; j < n; j++)
                {
                    if (Math.Abs(a[i, j]) > max)
                    {
                        max = Math.Abs(a[i, j]);
                        p = i;
                        q = j;
                    }
                }
            }
            if (max < 1e-14)
            {
                break;
            }

            var theta = 0.5 * Math.Atan2(2.0 * a[p, q], a[q, q] - a[p, p]);
            var c = Math.Cos(theta);
            var s = Math.Sin(theta);
            for (var k = 0; k < n; k++)
            {
                var akp = a[k, p];
                var akq = a[k, q];
                a[k, p] = c * akp - s * akq;
                a[k, q] = s * akp + c * akq;
            }
            for (var k = 0; k < n; k++)
            {
                var apk = a[p, k];
                var aqk = a[q, k];
                a[p, k] = c * apk - s * aqk;
                a[q, k] = s * apk + c * aqk;
            }
        }

        var values = new double[n];
        for (var i = 0; i < n; i++)
        {
            values[i] = a[i, i];
        }
        Array.Sort(values);
        return values;
    }

    /// <summary>Condition number of a (rows x cols) matrix via eigenvalues of AᵀA.</summary>
    public static double ConditionNumber(double[,] matrix)
    {
        var rows = matrix.GetLength(0);
        var cols = matrix.GetLength(1);
        var gram = new double[cols, cols];
        for (var i = 0; i < cols; i++)
        {
            for (var j = 0; j < cols; j++)
            {
                var sum = 0.0;
                for (var k = 0; k < rows; k++)
                {
                    sum += matrix[k, i] * matrix[k, j];
                }
                gram[i, j] = sum;
            }
        }
        var eigen = EigenvaluesSymmetric(gram);
        var min = Math.Max(eigen[0], 0.0);
        var maxEigen = eigen[^1];
        if (min < 1e-14)
        {
            return double.PositiveInfinity;
        }
        return Math.Sqrt(maxEigen / min);
    }

    public static double Mahalanobis(double[] residual, double[,] inverseCovariance)
    {
        var n = residual.Length;
        var sum = 0.0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                sum += residual[i] * inverseCovariance[i, j] * residual[j];
            }
        }
        return sum;
    }

    public static List<(double X, double Y)> ConvexHull(IReadOnlyList<(double X, double Y)> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count <= 2)
        {
            return sorted;
        }

        List<(double X, double Y)> Build(IEnumerable<(double X, double Y)> seq)
        {
            var hull = new List<(double X, double Y)>();
            foreach (var point in seq)
            {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 1e-15)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            return hull;
        }

        var lower = Build(sorted);
        var upper = Build(Enumerable.Reverse(sorted));
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public static double PolygonArea(IReadOnlyList<(double X, double Y)> polygon)
    {
        var area = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (x1, y1) = polygon[i];
            var (x2, y2) = polygon[(i + 1) % polygon.Count];
            area += x1 * y2 - x2 * y1;
        }
        return Math.Abs(area) / 2.0;
    }

    /// <summary>95% confidence ellipse parameters of a 2x2 covariance (chi2 = 5.991).</summary>
    public static (double Axis1, double Axis2, double AngleDeg) Ellipse95(double[,] covariance)
    {
        var a = covariance[0, 0];
        var b = covariance[0, 1];
        var c = covariance[1, 1];
        var mean = (a + c) / 2.0;
        var radius = Math.Sqrt(Math.Pow((a - c) / 2.0, 2) + b * b);
        var lambda1 = Math.Max(mean + radius, 0.0);
        var lambda2 = Math.Max(mean - radius, 0.0);
        var angle = 0.5 * Math.Atan2(2 * b, a - c) * 180.0 / Math.PI;
        return (Math.Sqrt(lambda1 * 5.991), Math.Sqrt(lambda2 * 5.991), angle);
    }
}

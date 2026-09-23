using System.Globalization;
using IsotopeMixing;

public static partial class MixingSolver
{
    private const int GridResolution = 36;

    public static AnalysisResult Solve(AnalysisInput input)
    {
        var diagnostics = new List<string>();
        var judgments = new List<MachineJudgment>();
        var dimensionCount = input.Dimensions.Count;
        var endmemberCount = input.ModesByEndmember.Count;
        var available = new List<int>();

        for (var d = 0; d < dimensionCount; d++)
        {
            if (double.IsFinite(input.Sample[d]) && Enumerable.Range(0, endmemberCount).All(i => input.ModesByEndmember[i].Modes.All(m => double.IsFinite(m.Values[d]))))
                available.Add(d);
        }

        var missing = input.Dimensions.Where((_, i) => !available.Contains(i)).ToList();
        if (missing.Count > 0)
            diagnostics.Add($"MISSING-DIMENSION: 未使用缺失维 {string.Join(", ", missing)}；该维仍保留在原始记录中。");

        var sample = available.Select(i => input.Sample[i]).ToArray();
        var covariance = MixingSolverMath.SubMatrix(input.Covariance, available);
        if (!MixingSolverMath.TryInverse(covariance, out var inverseCovariance, out var covarianceMessage))
        {
            return new AnalysisResult(
                StableIds.Hash("analysis", MixingSolverMath.Json(input)), "infeasible", "undetermined",
                "测量协方差不是正定矩阵，相关误差无法按协方差可靠传播。", input.Dimensions, missing,
                new[] { covarianceMessage }, Array.Empty<FeasibleRegion>(), MixingSolverMath.Dependency(input), null, null, null, MixingSolverMath.ChiSquareLimit(sample.Length),
                MixingSolverMath.Judgment("covariance-non-positive", "error", covarianceMessage));
        }

        var chiLimit = MixingSolverMath.ChiSquareLimit(sample.Length);
        var combinations = MixingSolverMath.Cartesian(input.ModesByEndmember.Select(x => x.Modes).ToArray());
        var regions = new List<FeasibleRegion>();

        foreach (var combo in combinations)
        {
            var matrix = MixingSolverMath.BuildMatrix(combo, available);
            var (ls, conditionRatio) = MixingSolverMath.ConstrainedLeastSquares(matrix, sample, endmemberCount);
            var points = new List<FeasiblePoint>();
            foreach (var p in MixingSolverMath.GridPoints(endmemberCount, GridResolution))
            {
                if (!MixingSolverMath.PassesPriors(p, input.Priors))
                    continue;
                var predicted = MixingSolverMath.Predict(combo, available, p);
                var residual = MixingSolverMath.Subtract(sample, predicted);
                var mahal = MixingSolverMath.Mahalanobis(residual, inverseCovariance);
                if (mahal <= chiLimit)
                    points.Add(new FeasiblePoint(StableIds.Hash("point", MixingSolverMath.PointKey(combo, p, input.AnalysisId)), MixingSolverMath.Round(p), Math.Round(Math.Sqrt(mahal), 6), MixingSolverMath.Round(residual)));
            }

            var modeIds = combo.Select(x => x.ModeId).ToArray();
            var modeKey = string.Join("|", modeIds);
            if (points.Count > 0)
            {
                var mins = Enumerable.Range(0, endmemberCount).Select(i => points.Min(p => p.Proportions[i])).ToArray();
                var maxs = Enumerable.Range(0, endmemberCount).Select(i => points.Max(p => p.Proportions[i])).ToArray();
                regions.Add(new FeasibleRegion(
                    StableIds.Hash("region", $"{input.AnalysisId}|{modeKey}"),
                    modeKey, modeIds, "feasible", points.OrderBy(p => p.Mahalanobis).ThenBy(p => p.StableId).ToList(),
                    MixingSolverMath.Round(mins), MixingSolverMath.Round(maxs), MixingSolverMath.ConvexVertices(points, endmemberCount)));
            }
            else
            {
                                regions.Add(new FeasibleRegion(
                    StableIds.Hash("region", $"{input.AnalysisId}|{modeKey}|empty"),
                    modeKey, modeIds, "empty", Array.Empty<FeasiblePoint>(),
                    Enumerable.Repeat(0d, endmemberCount).ToArray(), Enumerable.Repeat(0d, endmemberCount).ToArray(),
                    Array.Empty<FeasiblePoint>()));
            }
        }

        var feasibleRegions = regions.Where(r => r.Status == "feasible").ToList();
        var anyPriorConflict = input.Priors.Any(p => (p.Minimum.HasValue && p.Maximum.HasValue && p.Minimum > p.Maximum) || (p.Minimum.HasValue && (p.Minimum < 0 || p.Minimum > 1)) || (p.Maximum.HasValue && (p.Maximum < 0 || p.Maximum > 1)));
        var selectedModes = feasibleRegions.FirstOrDefault()?.ModeIds.Select(ModeById).ToArray()
            ?? input.ModesByEndmember[0].Modes.Take(1).ToArray();
        var activeMatrix = MixingSolverMath.BuildMatrix(selectedModes, available);
        var condition = MixingSolverMath.ConditionRatio(activeMatrix);
        var status = feasibleRegions.Count == 0 ? "infeasible" : feasibleRegions.Count > 1 ? "multiple-feasible" : "feasible";
        var (identifiability, rationale) = DescribeIdentifiability(activeMatrix, available.Count, endmemberCount, condition, feasibleRegions, anyPriorConflict);

        if (identifiability == "insufficient")
        {
            judgments.Add(MixingSolverMath.Judgment("endmembers-near-collinear", "warning",
                "端元在可用同位素方向上近共线或维数不足；比例未唯一识别，已保留全部满足约束的合法候选。",
                new Dictionary<string, string> { ["conditionRatio"] = condition?.ToString("G6", CultureInfo.InvariantCulture) ?? "unknown", ["candidateCount"] = feasibleRegions.Sum(r => r.Points.Count).ToString(CultureInfo.InvariantCulture) }));
        }
        judgments.Add(MixingSolverMath.Judgment("constraints-not-faked", "information",
            "候选比例由单纯形与先验直接生成，天然非负且和为一；未使用截断后归一化。",
            new Dictionary<string, string> { ["gridResolution"] = GridResolution.ToString(CultureInfo.InvariantCulture) }));
        judgments.Add(MixingSolverMath.Judgment("covariance-propagated", "information",
            "相关误差通过完整测量协方差的逆传播为马氏距离。",
            new Dictionary<string, string> { ["dimensions"] = string.Join("|", available.Select(i => input.Dimensions[i])) }));

        if (status == "multiple-feasible")
            judgments.Add(MixingSolverMath.Judgment("multiple-feasible-regions", "warning", "存在多个端元分布组合形成的可行区；未合并为单点答案。", new Dictionary<string,string> { ["regionCount"] = feasibleRegions.Count.ToString(CultureInfo.InvariantCulture) }));
        if (status == "infeasible")
            judgments.Add(MixingSolverMath.Judgment("sample-outside-envelope", "warning", "在当前分馏、协方差置信域和先验下，样品位于端元包络之外。", new Dictionary<string,string> { ["chiSquareLimit"] = chiLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        if (anyPriorConflict)
            judgments.Add(MixingSolverMath.Judgment("infeasible-prior", "error", "存在不可能满足的比例先验；该分支不能伪造可行解。", new Dictionary<string,string>()));

        return new AnalysisResult(
            StableIds.Hash("analysis", MixingSolverMath.Json(input) + string.Join("|", regions.Select(r => r.StableId))),
            status, identifiability, rationale, input.Dimensions, missing, diagnostics, regions,
            MixingSolverMath.Dependency(input), feasibleRegions.FirstOrDefault()?.Points.FirstOrDefault()?.Proportions,
            feasibleRegions.Count == 0 ? MixingSolverMath.Subtract(sample, MixingSolverMath.Predict(selectedModes, available, MixingSolverMath.ConstrainedLeastSquares(activeMatrix, sample, endmemberCount).Proportions)) : null,
            condition, chiLimit, judgments);

        EndmemberMode ModeById(string modeId) => input.ModesByEndmember.SelectMany(g => g.Modes).First(m => m.ModeId == modeId);
    }
}

internal static class MixingSolverMath
{
    public static string Json(AnalysisInput input) =>
        System.Text.Json.JsonSerializer.Serialize(input, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

    public static double[,] SubMatrix(double[,] source, IReadOnlyList<int> indices)
    {
        var result = new double[indices.Count, indices.Count];
        for (var r = 0; r < indices.Count; r++)
        for (var c = 0; c < indices.Count; c++)
            result[r, c] = source[indices[r], indices[c]];
        return result;
    }

    public static bool TryInverse(double[,] matrix, out double[,] inverse, out string message)
    {
        var n = matrix.GetLength(0);
        inverse = new double[n, n];
        var work = new double[n, n * 2];
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++) work[r, c] = matrix[r, c];
            work[r, n + r] = 1;
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(work[r, col]) > Math.Abs(work[pivot, col])) pivot = r;
            if (Math.Abs(work[pivot, col]) < 1e-12)
            {
                message = "COVARIANCE-SINGULAR: 协方差矩阵奇异，无法进行完整相关误差传播。";
                return false;
            }
            for (var c = 0; c < 2 * n; c++) (work[col, c], work[pivot, c]) = (work[pivot, c], work[col, c]);
            var scale = work[col, col];
            for (var c = 0; c < 2 * n; c++) work[col, c] /= scale;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var factor = work[r, col];
                for (var c = 0; c < 2 * n; c++) work[r, c] -= factor * work[col, c];
            }
        }

        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++)
            inverse[r, c] = work[r, n + c];
        for (var r = 0; r < n; r++)
        for (var c = r + 1; c < n; c++)
            if (Math.Abs(matrix[r, c] - matrix[c, r]) > 1e-10)
            {
                message = "COVARIANCE-ASYMMETRIC: 测量协方差不是对称矩阵。";
                return false;
            }
        for (var r = 0; r < n; r++)
        for (var c = 0; c < n; c++)
        {
            var quadratic = matrix[r, c] * inverse[r, c];
            if (!double.IsFinite(quadratic))
            {
                message = "COVARIANCE-NONFINITE: 协方差含非有限值。";
                return false;
            }
        }
        message = "";
        return true;
    }

    public static double ChiSquareLimit(int degreesOfFreedom) => degreesOfFreedom switch
    {
        1 => 3.841459,
        2 => 5.991465,
        3 => 7.814728,
        _ => 3.841459 + Math.Max(0, degreesOfFreedom - 1) * 2.0
    };

    public static List<EndmemberMode[]> Cartesian(IReadOnlyList<IReadOnlyList<EndmemberMode>> groups)
    {
        IEnumerable<EndmemberMode[]> current = new[] { Array.Empty<EndmemberMode>() };
        foreach (var group in groups)
        {
            var captured = group;
            current = current.SelectMany(prefix => captured.Select(mode => prefix.Append(mode).ToArray()));
        }
        return current.ToList();
    }

    public static double[,] BuildMatrix(IReadOnlyList<EndmemberMode> modes, IReadOnlyList<int> dimensions)
    {
        var matrix = new double[dimensions.Count, modes.Count];
        for (var d = 0; d < dimensions.Count; d++)
        for (var e = 0; e < modes.Count; e++)
            matrix[d, e] = modes[e].Values[dimensions[d]];
        return matrix;
    }

    public static IEnumerable<double[]> GridPoints(int count, int resolution)
    {
        foreach (var composition in Compositions(count, resolution))
            yield return composition.Select(x => x / (double)resolution).ToArray();
    }

    private static IEnumerable<int[]> Compositions(int count, int total)
    {
        if (count == 1)
        {
            yield return new[] { total };
            yield break;
        }
        for (var first = 0; first <= total; first++)
        foreach (var rest in Compositions(count - 1, total - first))
            yield return new[] { first }.Concat(rest).ToArray();
    }

    public static bool PassesPriors(double[] proportions, IReadOnlyList<PriorBound> priors)
    {
        foreach (var prior in priors)
        {
            var index = int.Parse(prior.EndmemberId.Split('.').Last()) - 1;
            if (index < 0 || index >= proportions.Length) return false;
            if (prior.Minimum is { } min && proportions[index] + 1e-9 < min) return false;
            if (prior.Maximum is { } max && proportions[index] - 1e-9 > max) return false;
        }
        return true;
    }

    public static double[] Predict(IReadOnlyList<EndmemberMode> modes, IReadOnlyList<int> dimensions, double[] proportions)
    {
        var result = new double[dimensions.Count];
        for (var d = 0; d < dimensions.Count; d++)
        for (var e = 0; e < modes.Count; e++)
            result[d] += modes[e].Values[dimensions[d]] * proportions[e];
        return result;
    }

    public static double[] Subtract(double[] left, double[] right) => left.Zip(right).Select(x => x.First - x.Second).ToArray();

    public static double Mahalanobis(double[] residual, double[,] inverseCovariance)
    {
        var total = 0d;
        for (var r = 0; r < residual.Length; r++)
        for (var c = 0; c < residual.Length; c++)
            total += residual[r] * inverseCovariance[r, c] * residual[c];
        return total;
    }

    public static (double[] Proportions, double? Score) ConstrainedLeastSquares(double[,] matrix, double[] sample, int endmemberCount)
    {
        var dimensions = matrix.GetLength(0);
        var best = new double[endmemberCount];
        best[^1] = 1;
        var bestScore = double.MaxValue;
        foreach (var p in GridPoints(endmemberCount, 72))
        {
            var predicted = new double[dimensions];
            for (var d = 0; d < dimensions; d++)
            for (var e = 0; e < endmemberCount; e++)
                predicted[d] += matrix[d, e] * p[e];
            var score = MixingSolverMath.Subtract(sample, predicted).Sum(x => x * x);
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return (best, Math.Sqrt(bestScore));
    }

    public static double? ConditionRatio(double[,] matrix)
    {
        var dimensions = matrix.GetLength(0);
        var endmembers = matrix.GetLength(1);
        if (endmembers <= 1) return null;
        var tangent = endmembers - 1;
        var gram = new double[tangent, tangent];
        for (var i = 0; i < tangent; i++)
        for (var j = 0; j < tangent; j++)
        for (var d = 0; d < dimensions; d++)
        {
            var ci = matrix[d, i] - matrix[d, endmembers - 1];
            var cj = matrix[d, j] - matrix[d, endmembers - 1];
            gram[i, j] += ci * cj;
        }
        var eigenvalues = Eigenvalues(gram);
        if (eigenvalues.Any(x => x <= 1e-8 * Math.Max(1, eigenvalues.Max()))) return null;
        return Math.Sqrt(eigenvalues.Max() / eigenvalues.Min());
    }

    private static double[] Eigenvalues(double[,] symmetric)
    {
        var n = symmetric.GetLength(0);
        var a = (double[,])symmetric.Clone();
        for (var sweep = 0; sweep < 60; sweep++)
        {
            var off = 0d;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++) off += a[p, q] * a[p, q];
            if (off < 1e-18) break;
            for (var p = 0; p < n; p++)
            for (var q = p + 1; q < n; q++)
            {
                if (Math.Abs(a[p, q]) < 1e-14) continue;
                var tau = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                var t = Math.Sign(tau == 0 ? 1 : tau) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                var c = 1 / Math.Sqrt(1 + t * t);
                var s = t * c;
                for (var k = 0; k < n; k++)
                {
                    var apk = a[p, k];
                    var aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
                for (var k = 0; k < n; k++)
                {
                    var akp = a[k, p];
                    var akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
            }
        }
        return Enumerable.Range(0, n).Select(i => a[i, i]).OrderBy(x => x).ToArray();
    }

    public static double[] Round(double[] values) => values.Select(x => Math.Round(x, 6)).ToArray();

    public static string PointKey(IReadOnlyList<EndmemberMode> combo, double[] proportions, string analysisId) =>
        analysisId + "|" + string.Join("|", combo.Select(x => x.ModeId)) + "|" + string.Join("|", proportions.Select(x => x.ToString("G8", CultureInfo.InvariantCulture)));

    public static IReadOnlyList<string> Dependency(AnalysisInput input) => new[]
    {
        input.SampleId, input.FractionationVersionId, input.PriorId,
        $"cov.{StableIds.Hash("cov", input.SampleId + input.FractionationVersionId)}"
    }.Concat(input.ModesByEndmember.SelectMany(x => x.Modes.Select(m => m.ModeId))).ToList();

    public static MachineJudgment Judgment(string code, string severity, string message, IReadOnlyDictionary<string, string> evidence) =>
        new MachineJudgment(code, severity, message, evidence);

    public static IReadOnlyList<FeasiblePoint> ConvexVertices(IReadOnlyList<FeasiblePoint> points, int endmemberCount)
    {
        if (points.Count <= 2 || endmemberCount < 2) return points.ToList();
        if (endmemberCount == 2)
            return new[] { points.MinBy(p => p.Proportions[0])!, points.MaxBy(p => p.Proportions[0])! };
        var ordered = points.OrderBy(p => p.Proportions[0]).ThenBy(p => p.Proportions[1]).ToList();
        var hull = new List<FeasiblePoint>();
        foreach (var point in ordered)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        var lower = hull.Count;
        for (var i = ordered.Count - 2; i >= 0; i--)
        {
            var point = ordered[i];
            while (hull.Count > lower && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static double Cross(FeasiblePoint a, FeasiblePoint b, FeasiblePoint c) =>
        (b.Proportions[0] - a.Proportions[0]) * (c.Proportions[1] - a.Proportions[1]) -
        (b.Proportions[1] - a.Proportions[1]) * (c.Proportions[0] - a.Proportions[0]);
}

public static partial class MixingSolver
{
    private static (string Identifiability, string Rationale) DescribeIdentifiability(
        double[,] matrix, int observedDimensions, int endmemberCount, double? conditionRatio,
        IReadOnlyList<FeasibleRegion> feasibleRegions, bool priorConflict)
    {
        if (priorConflict)
            return ("infeasible", "先验约束本身排除了整个单纯形；没有合法比例。");
        if (observedDimensions < endmemberCount - 1)
            return ("insufficient", $"只有 {observedDimensions} 个有效同位素方向而端元需要 {endmemberCount - 1} 个独立方向，存在自由混合维。");
        if (conditionRatio is null or > 30)
            return ("insufficient", conditionRatio is null
                ? "端元差向量存在线性相关，样品比例不能唯一识别。"
                : $"端元差矩阵条件比为 {conditionRatio:G3}，超过近共线阈值 30，候选区间对测量误差极敏感。");
        if (feasibleRegions.Count == 0)
            return ("infeasible", "端元方向可识别，但样品置信椭圆与分馏校正后的端元包络不相交。");
        if (feasibleRegions.Count > 1)
            return ("ambiguous", "多个端元分布组合分别形成可行区；需要人工裁定，不选择单点答案。");
        var region = feasibleRegions[0];
        var width = region.MaxProportions.Zip(region.MinProportions).Max(x => x.First - x.Second);
        if (width > 0.04)
            return ("interval", $"存在连续可行域，最宽比例区间为 {width:0.###}；系统展示区间而非点估计。");
        return ("point-like", "可行域很窄但仍以候选集合保存。");
    }
}

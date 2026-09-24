using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using IsotopeMixing.Core.Domain;
using IsotopeMixing.Core.Math;

namespace IsotopeMixing.Core.Engine;

public sealed class MixingEngine
{
    public const string RuleVersion = "mixing-engine-1.0.0";

    private const int NearCollinearGridPoints = 3;

    public AnalysisResult Analyze(EngineRequest request)
    {
        var findings = new List<Finding>();
        var diagnostics = new List<string>();

        var dimensions = request.EndmemberSet.DimensionOrder;
        var endmemberIds = request.EndmemberSet.Endmembers.Select(e => e.Id).ToArray();

        // 1) 样品在哪些维度有可解析测量；缺失维度保留在 MissingDimensions（不丢、不补零）。
        var measured = new List<string>();
        var sampleVector = new List<double>();
        foreach (var dim in dimensions)
        {
            var mv = request.Sample.Values.FirstOrDefault(v => v.DimensionId == dim);
            if (mv?.Parsed is double value && mv.ParseDiagnostic is null)
            {
                measured.Add(dim);
                sampleVector.Add(value);
            }
            else
            {
                findings.Add(new Finding(
                    FindingCode.MissingIsotopeDimension,
                    $"样品 {request.Sample.Label} 缺少同位素维度 {dim} 的可用测量。",
                    new Dictionary<string, object?> { ["dimension"] = dim, ["raw"] = mv?.Value, ["diagnostic"] = mv?.ParseDiagnostic }));
                diagnostics.Add(mv?.ParseDiagnostic ?? $"missing:{dim}");
            }
        }

        if (measured.Count == 0)
        {
            return Invalid(request, dimensions, measured, findings, diagnostics, "样品没有任何可解析的同位素测量。");
        }

        int k = measured.Count;

        // 2) 测量协方差：缺省时按各维单位方差兜底；提供非对角项即按相关误差传播。
        double[][] measCov = ResolveMeasurementCovariance(request, measured, findings);

        // 3) 分馏校正后的端元均值/方差（仅在被测维度上）。
        var modesByEndmember = ResolveModes(request, measured, diagnostics);
        if (modesByEndmember is null)
        {
            return Invalid(request, dimensions, measured, findings, diagnostics, "端元在被测维度上存在不可解析的值，无法构造混合模型。");
        }

        // 4) 先验区间（原样保留，不可行先验会得到空可行集并显式标记，而不是截断到边界）。
        var priorBounds = ResolvePriorBounds(request.Prior, endmemberIds);

        // 5) 凸包判定：样品是否落在分馏校正后的端元（第一模式均值）凸包内。
        var hullVertices = modesByEndmember.Select(m => m[0].Mean).ToArray();
        bool? inside = measured.Count <= 2 ? ConvexHull.Contains(hullVertices, sampleVector.ToArray()) : null;
        if (inside == false)
        {
            findings.Add(new Finding(
                FindingCode.SampleOutsideEnvelope,
                $"样品 {request.Sample.Label} 超出端元包络（被测维度：{string.Join(", ", measured)}）。",
                new Dictionary<string, object?> { ["dimensions"] = measured }));
        }

        // 6) 枚举各端元模式组合 -> 单纯形网格 -> 相关协方差下的 Mahalanobis 可行点。
        int endmemberCount = modesByEndmember.Length;
        int resolution = request.GridResolution ?? DefaultResolution(endmemberCount);
        double threshold = ChiSquare.CriticalValue(k, request.Confidence);

        var combos = EnumerateCombos(modesByEndmember);
        var feasible = new List<FeasiblePoint>();
        var bestByWeights = new Dictionary<string, FeasiblePoint>();

        foreach (var combo in combos)
        {
            foreach (var weights in Simplex.Grid(endmemberCount, resolution))
            {
                if (!WithinPrior(weights, priorBounds)) continue;

                var mean = new double[k];
                for (int d = 0; d < k; d++)
                {
                    for (int e = 0; e < endmemberCount; e++)
                        mean[d] += weights[e] * combo.Modes[e].Mean[d];
                }

                var totalCov = new double[k][];
                for (int a = 0; a < k; a++)
                {
                    totalCov[a] = new double[k];
                    for (int b = 0; b < k; b++)
                    {
                        totalCov[a][b] = measCov[a][b];
                        if (a == b)
                        {
                            for (int e = 0; e < endmemberCount; e++)
                                totalCov[a][b] += weights[e] * weights[e] * combo.Modes[e].Variance[a];
                        }
                    }
                }

                var residual = new double[k];
                for (int d = 0; d < k; d++) residual[d] = sampleVector[d] - mean[d];

                double mahalSq;
                try
                {
                    mahalSq = LinearAlgebra.QuadraticForm(totalCov, residual);
                }
                catch (InvalidOperationException)
                {
                    diagnostics.Add("covariance-not-positive-definite");
                    continue;
                }

                if (mahalSq <= threshold + 1e-9)
                {
                    var key = WeightKey(weights);
                    if (!bestByWeights.TryGetValue(key, out var existing) || mahalSq < existing.MahalanobisSq)
                    {
                        var point = new FeasiblePoint(weights, mahalSq, combo.Key, RegionIndex: -1);
                        bestByWeights[key] = point;
                    }
                }
            }
        }

        feasible.AddRange(bestByWeights.Values.OrderBy(p => WeightKey(p.Weights), StringComparer.Ordinal));

        return BuildResult(
            request, dimensions, measured, findings, diagnostics, priorBounds,
            combos, feasible, resolution, threshold, endmemberIds, sampleVector, measCov, inside);
    }

    private static int DefaultResolution(int endmemberCount) => endmemberCount switch
    {
        <= 2 => 80,
        3 => 30,
        _ => 12
    };

    private static double[][] ResolveMeasurementCovariance(EngineRequest request, List<string> measured, List<Finding> findings)
    {
        int k = measured.Count;
        var cov = new double[k][];
        for (int i = 0; i < k; i++) cov[i] = new double[k];

        if (request.Covariance is null)
        {
            for (int i = 0; i < k; i++) cov[i][i] = 1.0;
            return cov;
        }

        bool correlated = false;
        var covOrder = request.Covariance.DimensionOrder.ToArray();
        for (int i = 0; i < k; i++)
        {
            int ri = System.Array.IndexOf(covOrder, measured[i]);
            for (int j = 0; j < k; j++)
            {
                int rj = System.Array.IndexOf(covOrder, measured[j]);
                cov[i][j] = request.Covariance.Matrix[ri][rj];
                if (i != j && System.Math.Abs(cov[i][j]) > 1e-12) correlated = true;
            }
        }

        if (correlated)
        {
            findings.Add(new Finding(
                FindingCode.CorrelatedErrors,
                "测量误差存在非零相关，已按完整协方差传播（Mahalanobis 距离）。",
                new Dictionary<string, object?>
                {
                    ["dimensions"] = measured,
                    ["matrix"] = cov.Select(row => (object)row.ToArray()).ToArray()
                }));
        }
        return cov;
    }

    private List<ResolvedMode>[]? ResolveModes(EngineRequest request, List<string> measured, List<string> diagnostics)
    {
        var byEndmember = new List<ResolvedMode>[request.EndmemberSet.Endmembers.Count];

        for (int e = 0; e < request.EndmemberSet.Endmembers.Count; e++)
        {
            var record = request.EndmemberSet.Endmembers[e];
            var modes = new List<ResolvedMode>();

            if (record.Modes.Count > 0)
            {
                foreach (var mode in record.Modes)
                {
                    var mean = new double[measured.Count];
                    var variance = new double[measured.Count];
                    for (int d = 0; d < measured.Count; d++)
                    {
                        int globalIndex = System.Array.IndexOf(request.EndmemberSet.DimensionOrder.ToArray(), measured[d]);
                        double? rawMean = mode.Mean[globalIndex];
                        double rawVar = mode.Variance[globalIndex];
                        if (rawMean is null)
                        {
                            diagnostics.Add($"endmember-mode-missing:{record.Id}:{mode.ModeId}:{measured[d]}");
                            return null;
                        }
                        double factor = FractionationFor(request, record.Id, measured[d]);
                        mean[d] = rawMean.Value + factor;
                        variance[d] = rawVar;
                    }
                    modes.Add(new ResolvedMode(e, modes.Count, mean, variance, mode.Weight));
                }
            }
            else
            {
                var mean = new double[measured.Count];
                var variance = new double[measured.Count];
                for (int d = 0; d < measured.Count; d++)
                {
                    var meanValue = record.Means.FirstOrDefault(m => m.DimensionId == measured[d]);
                    var unc = record.Uncertainties.FirstOrDefault(u => u.DimensionId == measured[d]);
                    if (meanValue?.Parsed is not double parsed)
                    {
                        diagnostics.Add($"endmember-missing:{record.Id}:{measured[d]}:{meanValue?.ParseDiagnostic ?? "absent"}");
                        return null;
                    }
                    double factor = FractionationFor(request, record.Id, measured[d]);
                    mean[d] = parsed + factor;
                    double sd = unc?.Parsed ?? 0.5;
                    variance[d] = sd * sd;
                }
                modes.Add(new ResolvedMode(e, 0, mean, variance, 1.0));
            }

            byEndmember[e] = modes;
        }

        return byEndmember;
    }

    private static double FractionationFor(EngineRequest request, string endmemberId, string dimensionId)
    {
        var factor = request.Fractionation.Factors.FirstOrDefault(
            f => f.EndmemberId == endmemberId && f.DimensionId == dimensionId);
        return factor?.Parsed ?? 0.0;
    }

    private static (double Min, double Max)[] ResolvePriorBounds(PriorAssumption prior, string[] endmemberIds)
    {
        var bounds = new(double Min, double Max)[endmemberIds.Length];
        for (int i = 0; i < bounds.Length; i++)
        {
            var interval = prior.Intervals.FirstOrDefault(p => p.EndmemberId == endmemberIds[i]);
            bounds[i] = (interval?.Min ?? 0.0, interval?.Max ?? 1.0);
        }
        return bounds;
    }

    private static bool WithinPrior(double[] weights, (double Min, double Max)[] bounds)
    {
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] < bounds[i].Min - 1e-9 || weights[i] > bounds[i].Max + 1e-9) return false;
        }
        return true;
    }

    private static List<ModeCombo> EnumerateCombos(List<ResolvedMode>[] byEndmember)
    {
        var combos = new List<ModeCombo>();
        var current = new List<ResolvedMode>();
        int keyCounter = 0;

        void Recurse(int endmemberIndex, double weightProduct)
        {
            if (endmemberIndex == byEndmember.Length)
            {
                combos.Add(new ModeCombo(current.ToArray(), keyCounter, weightProduct));
                keyCounter++;
                return;
            }
            foreach (var mode in byEndmember[endmemberIndex])
            {
                current.Add(mode);
                Recurse(endmemberIndex + 1, weightProduct * mode.Weight);
                current.RemoveAt(current.Count - 1);
            }
        }

        Recurse(0, 1.0);
        return combos;
    }

    private static string WeightKey(double[] weights) =>
        string.Join("|", weights.Select(w => w.ToString("R")));

    private AnalysisResult BuildResult(
        EngineRequest request,
        IReadOnlyList<string> dimensions,
        List<string> measured,
        List<Finding> findings,
        List<string> diagnostics,
        (double Min, double Max)[] priorBounds,
        List<ModeCombo> combos,
        List<FeasiblePoint> feasible,
        int resolution,
        double threshold,
        string[] endmemberIds,
        List<double> sampleVector,
        double[][] measCov,
        bool? insideHull)
    {
        bool priorBlocksSimplex = PriorRulesOutAllWeights(priorBounds, endmemberIds.Length);

        if (priorBlocksSimplex)
        {
            findings.Add(new Finding(
                FindingCode.InfeasiblePrior,
                "先验区间与“非负且和为一”冲突；不存在任何合法权重，先验判定为不可行（未做截断/归一化）。",
                new Dictionary<string, object?>
                {
                    ["endmembers"] = endmemberIds,
                    ["bounds"] = priorBounds.Select(b => (object)new { min = b.Min, max = b.Max }).ToArray()
                }));
        }

        // 连通区（8-邻域）：多峰端元会得到多个互不相连的可行区。
        var regions = new List<FeasibleRegion>();
        if (feasible.Count > 0)
        {
            regions = BuildRegions(feasible, resolution, endmemberIds);
        }

        if (regions.Count == 0 && feasible.Count == 0)
        {
            findings.Add(new Finding(
                FindingCode.EmptyFeasibleSet,
                priorBlocksSimplex ? "不可行先验下可行集为空。" : "在测量协方差允许范围内没有任何合法混合权重。",
                new Dictionary<string, object?>
                {
                    ["thresholdChiSquare"] = threshold,
                    ["priorInfeasible"] = priorBlocksSimplex,
                    ["outsideEnvelope"] = insideHull == false
                }));
        }

        if (regions.Count > 1)
        {
            findings.Add(new Finding(
                FindingCode.MultipleFeasibleRegions,
                $"存在 {regions.Count} 个互不相连的可行区域；全部合法候选均保留，不压成单点。",
                new Dictionary<string, object?> { ["regionCount"] = regions.Count }));
        }
        else if (regions.Count == 1 && feasible.Count > 1)
        {
            findings.Add(new Finding(
                FindingCode.SingleFeasibleRegion,
                "存在一个连通可行区域，混合比例不唯一。",
                new Dictionary<string, object?> { ["gridPoints"] = feasible.Count }));
        }

        // 可识别性：端元（被测维度上）近共线时，显式报“不足”，绝不输出伪造的单点。
        bool underidentified = DetectNearCollinear(measured, combos[0], findings);
        bool dimensionLimited = measured.Count < dimensions.Count;

        var allCandidates = feasible
            .OrderBy(p => p.MahalanobisSq)
            .Select(p => new MixingCandidate(endmemberIds, p.Weights, p.MahalanobisSq, new[] { p.RegionIndex }))
            .ToList();

        // 为每个区域挑选最小 Mahalanobis 代表点（不下结论为唯一解）。
        var regionRecords = regions.Select(r =>
        {
            var reps = r.WeightPoints
                .Select(w => allCandidates.First(c => WeightKeysEqual(c.Weights, w)))
                .OrderBy(c => c.MahalanobisSquared)
                .Take(3)
                .ToList();
            return new FeasibleRegion(r.Index, r.Label, Downsample(r.WeightPoints.ToList()), reps, r.ModeComboKey);
        }).ToList();

        var residuals = BuildResiduals(feasible, combos, measured, sampleVector, threshold, endmemberIds);

        AnalysisStatus status;
        if (feasible.Count == 0)
            status = AnalysisStatus.Infeasible;
        else if (underidentified)
            status = AnalysisStatus.Underidentified;
        else if (dimensionLimited)
            status = AnalysisStatus.DimensionLimited;
        else if (feasible.Count == 1)
            status = AnalysisStatus.Unique;
        else
            status = AnalysisStatus.MultiCandidate;

        if (status == AnalysisStatus.Unique)
        {
            findings.Add(new Finding(FindingCode.IdentifiablePoint,
                "可行集仅含一个网格点，按当前分辨率该混合比例可唯一识别。",
                new Dictionary<string, object?> { ["resolution"] = resolution }));
        }

        var inputs = new AnalysisInputs(
            request.Sample.Id,
            request.EndmemberSet.Id,
            request.Fractionation.Id,
            request.Covariance?.Id ?? "cov:unit-fallback",
            request.Prior.Id);

        return new AnalysisResult(
            inputs,
            status,
            regionRecords,
            allCandidates,
            residuals,
            findings,
            measured,
            dimensions.Where(d => !measured.Contains(d)).ToArray(),
            RuleVersion,
            RuleHash);
    }

    private static bool PriorRulesOutAllWeights((double Min, double Max)[] bounds, int count)
    {
        double minSum = 0, maxSum = 0;
        foreach (var (min, max) in bounds)
        {
            if (min > max + 1e-9) return true;
            if (min < -1e-9 || max > 1 + 1e-9) return true;
            minSum += min;
            maxSum += max;
        }
        return minSum > 1 + 1e-9 || maxSum < 1 - 1e-9;
    }

    private bool DetectNearCollinear(List<string> measured, ModeCombo combo, List<Finding> findings)
    {
        int k = measured.Count;
        int m = combo.Modes.Count;

        var centroid = new double[k];
        foreach (var mode in combo.Modes)
            for (int d = 0; d < k; d++) centroid[d] += mode.Mean[d] / m;

        var design = new double[k][];
        for (int d = 0; d < k; d++)
        {
            design[d] = new double[m];
            for (int e = 0; e < m; e++) design[d][e] = combo.Modes[e].Mean[d] - centroid[d];
        }

        var singular = LinearAlgebra.SingularValues(design);
        double largest = singular[0];
        double smallest = singular[^1];
        double ratio = largest > 1e-12 ? smallest / largest : 0.0;

        bool collinear = m >= 3 && ratio < 0.02;
        if (collinear)
        {
            findings.Add(new Finding(
                FindingCode.NearCollinearEndmembers,
                $"端元在同位素空间近共线（奇异值比 s_min/s_max = {ratio:E2}），混合比例可识别性不足；仅报告可行域与全部候选。",
                new Dictionary<string, object?>
                {
                    ["singularValues"] = singular.Select(s => (object)s).ToArray(),
                    ["conditionRatio"] = ratio,
                    ["threshold"] = 0.02,
                    ["dimensions"] = measured
                }));
        }
        return collinear;
    }

    private static List<FeasibleRegion> BuildRegions(List<FeasiblePoint> feasible, int resolution, string[] endmemberIds)
    {
        int m = endmemberIds.Length;
        int keyDims = m - 1;
        var byIndex = new Dictionary<string, FeasiblePoint>();
        foreach (var point in feasible)
            byIndex[GridKey(point.Weights, resolution, keyDims)] = point;

        var visited = new HashSet<string>();
        var regions = new List<FeasibleRegion>();
        int regionIndex = 0;

        foreach (var seed in feasible)
        {
            string seedKey = GridKey(seed.Weights, resolution, keyDims);
            if (!visited.Add(seedKey)) continue;

            var queue = new Queue<string>();
            queue.Enqueue(seedKey);
            var members = new List<FeasiblePoint>();
            var comboKeys = new HashSet<int>();

            while (queue.Count > 0)
            {
                string currentKey = queue.Dequeue();
                var current = byIndex[currentKey];
                members.Add(current);
                comboKeys.Add(current.ComboKey);

                int[] idx = ParseKey(currentKey, keyDims);
                foreach (var neighbor in NeighborOffsets(keyDims))
                {
                    var candidateIdx = new int[keyDims];
                    int prefix = 0;
                    bool valid = true;
                    for (int d = 0; d < keyDims; d++)
                    {
                        candidateIdx[d] = idx[d] + neighbor[d];
                        if (candidateIdx[d] < 0 || candidateIdx[d] > resolution) { valid = false; break; }
                        prefix += candidateIdx[d];
                    }
                    if (!valid || prefix > resolution) continue;

                    string neighborKey = KeyFrom(candidateIdx);
                    if (byIndex.ContainsKey(neighborKey) && visited.Add(neighborKey))
                        queue.Enqueue(neighborKey);
                }
            }

            int representativeCombo = comboKeys.OrderBy(x => x).First();
            var weightPoints = members
                .OrderBy(p => WeightKey(p.Weights), StringComparer.Ordinal)
                .Select(p => p.Weights)
                .ToList();
            regions.Add(new FeasibleRegion(
                regionIndex,
                $"R{regionIndex + 1}",
                weightPoints,
                Array.Empty<MixingCandidate>(),
                representativeCombo));

            for (int i = 0; i < members.Count; i++)
            {
                var old = members[i];
                members[i] = new FeasiblePoint(old.Weights, old.MahalanobisSq, old.ComboKey, regionIndex);
                byIndex[GridKey(old.Weights, resolution, keyDims)] = members[i];
            }

            regionIndex++;
        }

        feasible.Clear();
        feasible.AddRange(byIndex.Values.OrderBy(p => WeightKey(p.Weights), StringComparer.Ordinal));
        return regions;
    }

    private static IEnumerable<int[]> NeighborOffsets(int dims)
    {
        int total = 1;
        for (int i = 0; i < dims; i++) total *= 3;
        for (int code = 0; code < total; code++)
        {
            int c = code;
            bool zero = true;
            var offset = new int[dims];
            for (int d = 0; d < dims; d++)
            {
                offset[d] = c % 3 - 1;
                c /= 3;
                if (offset[d] != 0) zero = false;
            }
            if (!zero) yield return offset;
        }
    }

    private static string GridKey(double[] weights, int resolution, int keyDims)
    {
        var idx = new int[keyDims];
        for (int d = 0; d < keyDims; d++) idx[d] = (int)System.Math.Round(weights[d] * resolution);
        return KeyFrom(idx);
    }

    private static string KeyFrom(int[] idx) => string.Join(",", idx);

    private static int[] ParseKey(string key, int dims)
    {
        var parts = key.Split(',');
        var idx = new int[dims];
        for (int i = 0; i < dims; i++) idx[i] = int.Parse(parts[i]);
        return idx;
    }

    private static List<double[]> Downsample(List<double[]> points, int max = 900)
    {
        if (points.Count <= max) return points;
        int stride = (int)System.Math.Ceiling(points.Count / (double)max);
        var sampled = new List<double[]>(points.Count / stride + 1);
        for (int i = 0; i < points.Count; i += stride) sampled.Add(points[i]);
        return sampled;
    }

    private static bool WeightKeysEqual(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (System.Math.Abs(a[i] - b[i]) > 1e-12) return false;
        return true;
    }

    private static List<ResidualReport> BuildResiduals(
        List<FeasiblePoint> feasible,
        List<ModeCombo> combos,
        List<string> measured,
        List<double> sampleVector,
        double threshold,
        string[] endmemberIds)
    {
        if (feasible.Count == 0) return new List<ResidualReport>();

        var best = feasible.OrderBy(p => p.MahalanobisSq).First();
        var combo = combos.First(c => c.Key == best.ComboKey);
        int k = measured.Count;
        var vector = new double[k];
        for (int d = 0; d < k; d++)
        {
            double mean = 0;
            for (int e = 0; e < endmemberIds.Length; e++)
                mean += best.Weights[e] * combo.Modes[e].Mean[d];
            vector[d] = sampleVector[d] - mean;
        }

        return new List<ResidualReport>
        {
            new(measured, vector, best.MahalanobisSq, threshold, best.MahalanobisSq <= threshold + 1e-9)
        };
    }

    private static AnalysisResult Invalid(
        EngineRequest request,
        IReadOnlyList<string> dimensions,
        List<string> measured,
        List<Finding> findings,
        List<string> diagnostics,
        string message)
    {
        findings.Insert(0, new Finding(
            FindingCode.EmptyFeasibleSet,
            message,
            new Dictionary<string, object?> { ["diagnostics"] = diagnostics }));

        var inputs = new AnalysisInputs(
            request.Sample.Id,
            request.EndmemberSet.Id,
            request.Fractionation.Id,
            request.Covariance?.Id ?? "cov:unit-fallback",
            request.Prior.Id);

        return new AnalysisResult(
            inputs,
            AnalysisStatus.InvalidInput,
            Array.Empty<FeasibleRegion>(),
            Array.Empty<MixingCandidate>(),
            Array.Empty<ResidualReport>(),
            findings,
            measured,
            dimensions.Where(d => !measured.Contains(d)).ToArray(),
            RuleVersion,
            RuleHash);
    }

    public static string RuleHash
    {
        get
        {
            const string material = RuleVersion + "|simplex-grid+mahalanobis+chi2-0.95+8n-connected-regions|no-clip-no-renormalize";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}

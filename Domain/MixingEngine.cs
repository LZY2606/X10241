using System.Globalization;
using System.Text;

namespace IsotopeMix.Domain;

public sealed class MixingEngine
{
    private const double IdentifiabilityRatio = 1e-3;

    public AnalysisResult Run(AnalysisInput input)
    {
        var events = new List<EngineEvent>();
        var dims = BuildDimensions(input, events);
        int[] activeIdx = input.ActiveDimensions.ToArray();
        int d = activeIdx.Length;
        int n = input.Sources.Count;

        var (s, sampleSigma, sampleMean, sourceSigmas) = AssembleMatrices(input, activeIdx);
        double[] y = sampleMean;

        var (lower, upper, priorInfeasible, activePriorRegions) = ApplyPrior(input.Prior, n, events);

        var envelope = ComputeEnvelope(input, s, d, events);
        var (point, identified, smallestRatio) =
            SolvePoint(s, y, sampleSigma, sourceSigmas, lower, upper, d, n, events);

        double threshold = Statistics.ChiSquareQuantile(input.ConfidenceLevel, d);

        var candidates = BuildCandidates(
            s, y, sampleSigma, lower, upper, activePriorRegions,
            threshold, d, n, point, identified, input.Prior.Empty, priorInfeasible, events);

        var residuals = ComputeResiduals(input, activeIdx, s, y, point, sampleSigma);

        if (identified && point is not null)
        {
            for (int ci = 0; ci < candidates.Count; ci++)
            {
                bool contains = candidates[ci].Rings.Any(ring => PointInRing(point.Proportions, ring));
                if (contains)
                {
                    candidates[ci] = candidates[ci] with { ContainsPointEstimate = true };
                    break;
                }
            }
        }

        if (identified && point is { RepresentativeOnly: false })
        {
            events.Add(new EngineEvent("POINT_ESTIMATE_UNIQUE", "info",
                $"sample '{input.SampleId}': proportions are identifiable under the active dimensions",
                new Dictionary<string, object?>
                {
                    ["sampleId"] = input.SampleId,
                    ["proportions"] = point.Proportions,
                    ["chi2"] = point.Chi2,
                    ["sourceOrder"] = input.Sources.Select(src => src.SourceId).ToArray(),
                    ["smallestSingularRatio"] = smallestRatio,
                    ["kktResidual"] = point.KktResidual
                },
                new[] { input.SampleId }));
        }

        double chi2Min = point?.Chi2 ?? (candidates.Count > 0 ? candidates.Min(c => c.Chi2Minimum) : double.PositiveInfinity);
        if (double.IsFinite(chi2Min) && chi2Min > threshold * (1 + 1e-9) && !priorInfeasible && !input.Prior.Empty)
        {
            events.Add(new EngineEvent("FEASIBILITY_EMPTY", "warning",
                $"sample '{input.SampleId}': no mixture satisfies the confidence ellipse " +
                $"(min chi2 {chi2Min:G4} > threshold {threshold:G4})",
                new Dictionary<string, object?>
                {
                    ["sampleId"] = input.SampleId, ["chi2Minimum"] = chi2Min,
                    ["chi2Threshold"] = threshold, ["confidenceLevel"] = input.ConfidenceLevel
                },
                new[] { input.SampleId }));
        }

        return new AnalysisResult
        {
            CaseId = input.CaseId,
            SampleId = input.SampleId,
            FractionationVersionId = input.Fractionation.VersionId,
            SourceVersionId = input.SourceVersionId,
            PriorVersionId = input.Prior.VersionId,
            ConfidenceLevel = input.ConfidenceLevel,
            DegreesOfFreedom = d,
            Chi2Threshold = threshold,
            Dimensions = dims,
            EffectiveSources = s,
            Observed = y,
            Envelope = envelope,
            Point = point,
            Residuals = residuals,
            Candidates = candidates,
            Events = events
        };
    }

    private static List<DimensionInfo> BuildDimensions(AnalysisInput input, List<EngineEvent> events)
    {
        var list = new List<DimensionInfo>();
        foreach (var axis in input.Axes)
        {
            bool active = input.ActiveDimensions.Contains(input.Axes.IndexOf(axis));
            if (active)
            {
                list.Add(new DimensionInfo(axis.Key, true, null));
            }
            else
            {
                var q = input.Sample.Values[input.Axes.IndexOf(axis)];
                string reason = q.Diagnostic ?? "missing isotope dimension";
                list.Add(new DimensionInfo(axis.Key, false, reason));
                events.Add(new EngineEvent("DIMENSION_DROPPED", "warning",
                    $"dimension '{axis.Key}' retained in the original record but excluded from computation: {reason}",
                    new Dictionary<string, object?>
                    {
                        ["axisKey"] = axis.Key, ["raw"] = q.Raw,
                        ["unit"] = q.Unit, ["datum"] = q.Datum, ["reason"] = reason
                    },
                    new[] { input.Sample.SampleId }));
            }
        }
        return list;
    }

    private (double[][] s, double[][] sigma, double[] y, double[][][] sourceSigmas) AssembleMatrices(
        AnalysisInput input, int[] activeIdx)
    {
        int d = activeIdx.Length;
        int n = input.Sources.Count;
        var s = LinearAlgebra.Matrix(d, n);
        var sourceCov = Enumerable.Range(0, n).Select(_ => LinearAlgebra.Matrix(d, d)).ToArray();

        for (int si = 0; si < n; si++)
        {
            var src = input.Sources[si];
            var shifts = input.Fractionation.ShiftsBySource.GetValueOrDefault(src.SourceId)
                         ?? input.Fractionation.ShiftsBySource.GetValueOrDefault("*")
                         ?? Enumerable.Repeat<double?>(0, input.Axes.Count).ToArray();
            for (int ai = 0; ai < d; ai++)
            {
                int axisPos = activeIdx[ai];
                s[ai][si] = src.Values[axisPos].Value!.Value + (shifts[axisPos] ?? 0);
                if (src.Covariance is not null)
                {
                    var cell = src.Covariance[axisPos][axisPos];
                    if (cell is not null) sourceCov[si][ai][ai] = cell.Value;
                }
            }
        }

        var sigma = LinearAlgebra.Matrix(d, d);
        for (int a = 0; a < d; a++)
            for (int b = 0; b < d; b++)
                sigma[a][b] = input.Sample.Covariance[activeIdx[a]][activeIdx[b]] ?? 0;

        var y = new double[d];
        for (int a = 0; a < d; a++) y[a] = input.Sample.Values[activeIdx[a]].Value!.Value;
        return (s, sigma, y, sourceCov);
    }

    private static (double[] lower, double[] upper, bool infeasible, List<(double[], double[])>) ApplyPrior(
        PriorVersion prior, int n, List<EngineEvent> events)
    {
        var regions = new List<(double[] lo, double[] up)>();
        if (!prior.Empty)
        {
            foreach (var region in prior.Regions)
            {
                var lo = Enumerable.Repeat(0.0, n).ToArray();
                var up = Enumerable.Repeat(1.0, n).ToArray();
                int k = 0;
                foreach (var (sourceId, range) in region.Bounds)
                {
                    int idx = int.Parse(sourceId.StartsWith("src") ? sourceId[3..] : sourceId,
                        CultureInfo.InvariantCulture) - 1;
                    if (idx is >= 0 and < n) // bounds keyed by source ordinal: src1..srcN
                    {
                        lo[idx] = range.lo;
                        up[idx] = range.hi;
                        k++;
                    }
                }
                regions.Add((lo, up));
            }
        }

        bool infeasible = false;
        if (prior.Empty)
        {
            infeasible = true;
            events.Add(new EngineEvent("PRIOR_INFEASIBLE", "error",
                $"prior '{prior.VersionId}' is declared empty: no proportions are admissible",
                new Dictionary<string, object?>
                {
                    ["priorVersionId"] = prior.VersionId, ["reason"] = "declared empty set"
                },
                Array.Empty<string>()));
        }
        else
        {
            var feasibleRegions = new List<(double[], double[])>();
            foreach (var (lo, up) in regions)
            {
                var qp = new QuadraticProgram(
                    LinearAlgebra.Matrix(n, n), new double[n], lo, up);
                if (qp.IsFeasible()) feasibleRegions.Add((lo, up));
                else
                {
                    events.Add(new EngineEvent("PRIOR_INFEASIBLE", "error",
                        $"prior '{prior.VersionId}' contains a region inconsistent with " +
                        "sum-to-one non-negative proportions",
                        new Dictionary<string, object?>
                        {
                            ["priorVersionId"] = prior.VersionId,
                            ["lower"] = lo, ["upper"] = up,
                            ["lowerSum"] = lo.Sum(), ["upperSum"] = up.Sum()
                        },
                        Array.Empty<string>()));
                }
            }
            if (feasibleRegions.Count == 0) infeasible = true;
            else regions = feasibleRegions;

            if (!infeasible && regions.Count > 1)
            {
                events.Add(new EngineEvent("FEASIBLE_REGION_MULTIPLE", "info",
                    $"prior '{prior.VersionId}' defines {regions.Count} disjoint admissible regions; " +
                    "each is kept as an independent candidate",
                    new Dictionary<string, object?>
                    {
                        ["priorVersionId"] = prior.VersionId, ["regionCount"] = regions.Count
                    },
                    Array.Empty<string>()));
            }
        }

        var mergedLo = Enumerable.Repeat(0.0, n).ToArray();
        var mergedUp = Enumerable.Repeat(1.0, n).ToArray();
        return (mergedLo, mergedUp, infeasible, infeasible ? new() : regions);
    }

    private EnvelopeInfo ComputeEnvelope(AnalysisInput input, double[][] s, int d, List<EngineEvent> events)
    {
        if (d < 1) return new EnvelopeInfo(false, null, null);
        if (d > 2) return new EnvelopeInfo(false, null, null);

        int n = s[0].Length;
        var hull = ConvexHull(s, d);
        double[] y = input.Sample.Values
            .Where((_, i) => input.ActiveDimensions.Contains(i))
            .Select(q => q.Value!.Value).ToArray();

        bool inside = true;
        double margin = double.PositiveInfinity;
        for (int e = 0; e < hull.Count; e++)
        {
            int a = hull[e], b = hull[(e + 1) % hull.Count];
            double ex, ey, nx, ny, cx, cy;
            if (d == 2)
            {
                ex = s[0][b] - s[0][a]; ey = s[1][b] - s[1][a];
                nx = ey; ny = -ex;
                double len = Math.Sqrt(nx * nx + ny * ny);
                if (len < 1e-15) continue;
                nx /= len; ny /= len;
                cx = s[0][a]; cy = s[1][a];
            }
            else
            {
                ex = 0; ey = 0; nx = 0;
                double lo = Math.Min(s[0][a], s[0][b]);
                double hi = Math.Max(s[0][a], s[0][b]);
                margin = Math.Min(margin, y[0] < lo ? lo - y[0] : y[0] > hi ? y[0] - hi : 0);
                if (y[0] < lo - 1e-12 || y[0] > hi + 1e-12) inside = false;
                continue;
            }

            double dist = nx * (y[0] - cx) + ny * (y.Length > 1 ? y[1] - cy : 0);
            // 边内点应给出非负距离（CCW 凸包）；负距离表示在外侧。
            double sign = 0;
            for (int k = 0; k < hull.Count; k++)
                sign += nx * (s[0][hull[k]] - cx) + ny * (s[1][hull[k]] - cy);
            if (sign < 0) { nx = -nx; ny = -ny; dist = -dist; }
            margin = Math.Min(margin, dist);
            if (dist < -1e-9) inside = false;
        }

        if (!inside)
        {
            events.Add(new EngineEvent("SAMPLE_OUTSIDE_ENVELOPE", "warning",
                $"sample '{input.SampleId}' lies outside the endmember envelope " +
                $"(signed margin {margin:G4})",
                new Dictionary<string, object?>
                {
                    ["sampleId"] = input.SampleId,
                    ["margin"] = margin,
                    ["interpretation"] =
                        "no exact mixture exists; correlated measurement error may still permit a confidence-level solution"
                },
                new[] { input.SampleId }));
        }
        return new EnvelopeInfo(true, !inside, margin);
    }

    private static List<int> ConvexHull(double[][] s, int d)
    {
        if (d == 1) return new List<int> { 0, s[0].Length - 1 };
        int n = s[0].Length;
        var pts = Enumerable.Range(0, n)
            .OrderBy(i => s[0][i]).ThenBy(i => s[1][i]).ToList();
        int Cross(int o, int a, int b) =>
            Math.Sign((s[0][a] - s[0][o]) * (s[1][b] - s[1][o]) -
                      (s[1][a] - s[1][o]) * (s[0][b] - s[0][o]));

        var lower = new List<int>();
        foreach (int p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<int>();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            int p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private (PointEstimate? point, bool identified, double ratio) SolvePoint(
        double[][] s, double[] y, double[][] sampleSigma, double[][][] sourceSigmas,
        double[] lower0, double[] upper0, int d, int n, List<EngineEvent> events)
    {
        var (sv, _) = LinearAlgebra.Svd(s);
        double smax = sv[0];
        double ratio = smax > 0 ? sv[^1] / smax : 0;
        bool rankDeficient = sv[^1] < IdentifiabilityRatio * (smax + 1e-300);
        bool underDetermined = d < n - 1 || rankDeficient;

        if (underDetermined)
        {
            string reason = d < n - 1
                ? $"only {d} active isotope dimension(s) for {n} sources (need at least {n - 1})"
                : $"endmember columns are near-collinear (smallest singular ratio {ratio:E2})";
            events.Add(new EngineEvent("IDENTIFIABILITY_INSUFFICIENT", "error",
                "mixture proportions are not uniquely identifiable; the full admissible set is reported " +
                "instead of a fabricated point estimate",
                new Dictionary<string, object?>
                {
                    ["singularValues"] = sv,
                    ["smallestSingularRatio"] = ratio,
                    ["activeDimensions"] = d,
                    ["sourceCount"] = n,
                    ["rule"] = "no clamp-and-renormalize; constraints sum(pi)=1, pi>=0 are enforced by the QP projection"
                },
                Array.Empty<string>()));
            return (null, false, ratio);
        }

        // 迭代再加权：端元协方差通过混合比例进入总协方差，相关误差按完整协方差矩阵传播。
        double[] x = Enumerable.Repeat(1.0 / n, n).ToArray();
        double chi2 = double.PositiveInfinity;
        double kkt = double.PositiveInfinity;
        double[][] w = sampleSigma;

        for (int iter = 0; iter < 40; iter++)
        {
            double[][] cov = TotalCovariance(sampleSigma, sourceSigmas, x, d);
            if (!LinearAlgebra.SolvePositiveDefinite(cov, new double[d], out _))
            {
                cov = Regularize(cov, 1e-10);
            }
            w = InvertSymmetric(cov);
            var h = LinearAlgebra.Multiply(LinearAlgebra.Multiply(Transpose(s), w), s);
            var sy = LinearAlgebra.Multiply(s, y);
            var f = new double[n];
            var ws = LinearAlgebra.Multiply(w, s);
            var wy = LinearAlgebra.Multiply(w, y);
            for (int i = 0; i < n; i++)
            {
                double sum = 0;
                for (int a = 0; a < d; a++) sum += s[a][i] * wy[a];
                f[i] = -sum;
            }
            var qp = new QuadraticProgram(h, f, lower0, upper0);
            if (!qp.IsFeasible()) return (null, true, ratio);
            var (nx, _, residual) = qp.Solve();
            double move = 0;
            for (int i = 0; i < n; i++) move = Math.Max(move, Math.Abs(nx[i] - x[i]));
            x = nx;
            kkt = residual;
            var residualVec = Diff(LinearAlgebra.Multiply(s, x), y);
            double newChi2 = QuadraticForm(w, residualVec);
            if (move < 1e-11 && Math.Abs(newChi2 - chi2) < 1e-11 * Math.Max(1, newChi2))
            {
                chi2 = newChi2;
                break;
            }
            chi2 = newChi2;
        }

        double[]? se = null;
        try
        {
            var covEnd = TotalCovariance(sampleSigma, sourceSigmas, x, d);
            var wEnd = InvertSymmetric(covEnd);
            var hEnd = LinearAlgebra.Multiply(LinearAlgebra.Multiply(Transpose(s), wEnd), s);
            // 内部方向（sum=1 的零空间）上的 Hessian 子块逆，给出内部比例的线性化标准误。
            if (n >= 2)
            {
                var sub = LinearAlgebra.Matrix(n - 1, n - 1);
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < n - 1; j++)
                        sub[i][j] = hEnd[i][j] - hEnd[i][n - 1] - hEnd[n - 1][j] + hEnd[n - 1][n - 1];
                if (LinearAlgebra.SolvePositiveDefinite(sub, new double[n - 1], out _))
                {
                    var inv = InvertSymmetric(sub);
                    se = new double[n];
                    for (int i = 0; i < n - 1; i++) se[i] = Math.Sqrt(Math.Max(0, inv[i][i]));
                    double last = 0;
                    for (int i = 0; i < n - 1; i++) last += inv[i][i];
                    double cross = 0;
                    for (int i = 0; i < n - 1; i++)
                        for (int j = 0; j < n - 1; j++)
                            cross += inv[i][j];
                    se[n - 1] = Math.Sqrt(Math.Max(0, cross));
                }
            }
        }
        catch
        {
            se = null;
        }

        var point = new PointEstimate(x, chi2, true, false, se, kkt);
        return (point, true, ratio);
    }

    private static double[][] TotalCovariance(double[][] sampleSigma, double[][][] sourceSigmas, double[] x, int d)
    {
        // Σ(x) = Σ_sample + Σ_k x_k² Σ_source,k；相关误差按完整协方差矩阵传播，绝不简化成对角矩阵。
        var cov = sampleSigma.Select(r => (double[])r.Clone()).ToArray();
        for (int si = 0; si < sourceSigmas.Length; si++)
        {
            double w = x[si] * x[si];
            for (int a = 0; a < d; a++)
                for (int b = 0; b < d; b++)
                    cov[a][b] += w * sourceSigmas[si][a][b];
        }
        return cov;
    }

    private static double[][] Regularize(double[][] m, double eps)
    {
        var r = m.Select(row => (double[])row.Clone()).ToArray();
        for (int i = 0; i < r.Length; i++) r[i][i] += eps;
        return r;
    }

    private static double[][] InvertSymmetric(double[][] m)
    {
        int n = m.Length;
        var inv = LinearAlgebra.Matrix(n, n);
        for (int col = 0; col < n; col++)
        {
            var e = new double[n];
            e[col] = 1;
            var augmented = m.Select(r => (double[])r.Clone()).ToArray();
            LinearAlgebra.SolvePositiveDefinite(augmented, e, out var sol);
            for (int row = 0; row < n; row++) inv[row][col] = sol[row];
        }
        return inv;
    }

    private static double QuadraticForm(double[][] w, double[] v)
    {
        var wv = LinearAlgebra.Multiply(w, v);
        double sum = 0;
        for (int i = 0; i < v.Length; i++) sum += v[i] * wv[i];
        return sum;
    }

    private static double[] Diff(double[] a, double[] b)
    {
        var d = new double[a.Length];
        for (int i = 0; i < a.Length; i++) d[i] = a[i] - b[i];
        return d;
    }

    private List<CandidateInfo> BuildCandidates(
        double[][] s, double[] y, double[][] sampleSigma,
        double[] lower, double[] upper, List<(double[] lo, double[] up)> regions,
        double threshold, int d, int n,
        PointEstimate? point, bool identified, bool priorEmpty, bool priorInfeasible,
        List<EngineEvent> events)
    {
        var result = new List<CandidateInfo>();
        if (priorEmpty || priorInfeasible) return result;
        if (d == 0)
        {
            double[][] full =
            {
                new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 }
            };
            result.Add(new CandidateInfo("cand-1", "All proportions admissible (no usable isotope dimension)",
                new List<PolygonRing> { new(full.ToList()) }, new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 },
                1, 0, point is not null, null));
            return result;
        }

        const int resolution = 90;
        int count = 0;
        foreach (var (lo, up) in regions)
        {
            var grid = new double[resolution + 1, resolution + 1];
            var feasible = new bool[resolution + 1, resolution + 1];
            for (int i = 0; i <= resolution; i++)
            {
                for (int j = 0; j <= resolution; j++)
                {
                    double p1 = (double)i / resolution;
                    double p2 = (double)j / resolution;
                    double[] x;
                    bool inBounds = true;
                    if (n == 1)
                    {
                        x = new[] { 1.0 };
                    }
                    else if (n == 2)
                    {
                        x = new[] { p1, 1 - p1 };
                        inBounds = j == 0;
                    }
                    else
                    {
                        x = new[] { p1, p2, 1 - p1 - p2 };
                        if (p1 + p2 > 1 + 1e-12) inBounds = false;
                    }
                    for (int k = 0; k < n && inBounds; k++)
                        if (x[k] < lo[k] - 1e-9 || x[k] > up[k] + 1e-9) inBounds = false;
                    feasible[i, j] = inBounds;
                    if (!inBounds) { grid[i, j] = double.PositiveInfinity; continue; }

                    var cov = sampleSigma.Select(r => (double[])r.Clone()).ToArray();
                    if (!LinearAlgebra.SolvePositiveDefinite(cov, new double[d], out _))
                        cov = Regularize(cov, 1e-10);
                    var w = InvertSymmetric(cov);
                    var pred = LinearAlgebra.Multiply(s, x);
                    grid[i, j] = QuadraticForm(w, Diff(pred, y));
                }
            }

            var rings = MarchingSquares(grid, feasible, threshold, resolution, n);
            double insideCount = 0, totalFeasible = 0;
            for (int i = 0; i <= resolution; i++)
                for (int j = 0; j <= resolution; j++)
                {
                    if (!feasible[i, j]) continue;
                    totalFeasible++;
                    if (grid[i, j] <= threshold) insideCount++;
                }

            if (rings.Count > 0)
            {
                double chi2Min = double.PositiveInfinity;
                foreach (var ring in rings)
                    foreach (var pt in ring.BarycentricPoints)
                    {
                        var cov = sampleSigma.Select(r => (double[])r.Clone()).ToArray();
                        var w = InvertSymmetric(cov);
                        chi2Min = Math.Min(chi2Min,
                            QuadraticForm(w, Diff(LinearAlgebra.Multiply(s, pt), y)));
                    }
                var centroid = rings[0].Centroid;
                string id = $"cand-{++count}";
                result.Add(new CandidateInfo(
                    id,
                    regions.Count > 1 ? $"Disjoint admissible region {count}" : "Confidence-level feasible region",
                    rings,
                    centroid,
                    totalFeasible > 0 ? insideCount / totalFeasible : 0,
                    chi2Min,
                    false, null));
            }
        }

        if (result.Count == 0 && !priorEmpty && !priorInfeasible)
        {
            // 空候选可能仅表示置信椭圆不落先验；FEASIBILITY_EMPTY 由最小 chi2 与阈值比较统一发出。
        }
        return result;
    }

    /// <summary>
    /// Marching squares：在比例单纯形网格上抽取 χ²=阈值 的等值环，坐标以重心坐标返回。
    /// 鞍点按中点分裂，保证多个不连通可行区生成多个独立环。
    /// </summary>
    private static List<PolygonRing> MarchingSquares(
        double[,] grid, bool[,] feasible, double threshold, int resolution, int n)
    {
        var segments = new List<(double[] a, double[] b)>();

        double[] ToBary(double gx, double gy)
        {
            if (n == 2) return new[] { gx, 1 - gx };
            return new[] { gx, gy, 1 - gx - gy };
        }

        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                double v00 = grid[i, j], v10 = grid[i + 1, j];
                double v01 = grid[i, j + 1], v11 = grid[i + 1, j + 1];
                if (!feasible[i, j]) v00 = double.PositiveInfinity;
                if (!feasible[i + 1, j]) v10 = double.PositiveInfinity;
                if (!feasible[i, j + 1]) v01 = double.PositiveInfinity;
                if (!feasible[i + 1, j + 1]) v11 = double.PositiveInfinity;
                if (new[] { v00, v10, v11, v01 }.All(v => double.IsInfinity(v))) continue;

                int mask = 0;
                if (v00 <= threshold) mask |= 1;
                if (v10 <= threshold) mask |= 2;
                if (v11 <= threshold) mask |= 4;
                if (v01 <= threshold) mask |= 8;
                if (mask is 0 or 15) continue;

                double t01(double va, double vb) =>
                    double.IsInfinity(va) ? 0 : double.IsInfinity(vb) ? 1 :
                    (threshold - va) / (vb - va);
                double step = 1.0 / resolution;
                double[] bottom = ToBary((i + t01(v00, v10)) * step, (double)j / resolution);
                double[] right = ToBary((i + 1.0) * step, (j + t01(v10, v11)) * step);
                double[] top = ToBary((i + t01(v01, v11)) * step, (j + 1.0) * step);
                double[] left = ToBary((double)i * step, (j + t01(v00, v01)) * step);

                void Add(double[] a, double[] b) => segments.Add((a, b));
                switch (mask)
                {
                    case 1: case 14: Add(bottom, left); break;
                    case 2: case 13: Add(bottom, right); break;
                    case 3: case 12: Add(left, right); break;
                    case 4: case 11: Add(right, top); break;
                    case 6: case 9: Add(bottom, top); break;
                    case 7: case 8: Add(left, top); break;
                    case 5:
                        var center = ToBary((i + 0.5) * step, (j + 0.5) * step);
                        Add(bottom, center); Add(center, left); Add(right, center); Add(center, top);
                        break;
                    case 10:
                        var c2 = ToBary((i + 0.5) * step, (j + 0.5) * step);
                        Add(bottom, c2); Add(c2, right); Add(left, c2); Add(c2, top);
                        break;
                }
            }
        }

        return LinkSegments(segments);
    }

    private static List<PolygonRing> LinkSegments(List<(double[] a, double[] b)> segments)
    {
        const double eps = 1.5e-3;
        var unused = new List<(double[] a, double[] b)>(segments);
        var rings = new List<PolygonRing>();

        while (unused.Count > 0)
        {
            var ring = new List<double[]>();
            var current = unused[0];
            unused.RemoveAt(0);
            ring.Add(current.a);
            ring.Add(current.b);

            bool growing = true;
            while (growing)
            {
                growing = false;
                for (int k = 0; k < unused.Count; k++)
                {
                    var tail = ring[^1];
                    if (Close(tail, unused[k].a, eps)) { ring.Add(unused[k].b); unused.RemoveAt(k); growing = true; break; }
                    if (Close(tail, unused[k].b, eps)) { ring.Add(unused[k].a); unused.RemoveAt(k); growing = true; break; }
                }
                if (ring.Count > 2 && Close(ring[0], ring[^1], eps)) break;
                if (ring.Count > 40000) break;
            }
            if (ring.Count >= 3 && rings.All(r => r.BarycentricPoints.Count != ring.Count))
                rings.Add(new PolygonRing(ring));
            else if (ring.Count >= 3)
                rings.Add(new PolygonRing(ring));
        }
        return rings;
    }

    private static bool Close(double[] a, double[] b, double eps)
    {
        double d = 0;
        for (int i = 0; i < a.Length; i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d < eps;
    }

    private static bool PointInRing(double[] p, PolygonRing ring)
    {
        // 二维重心坐标（p1, p2）内点检测，第三个分量隐含。
        bool inside = false;
        var pts = ring.BarycentricPoints;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            double xi = pts[i][0], yi = pts[i][1];
            double xj = pts[j][0], yj = pts[j][1];
            bool intersect = (yi > p[1]) != (yj > p[1]) &&
                             p[0] < (xj - xi) * (p[1] - yi) / ((yj - yi) + 1e-300) + xi;
            if (intersect) inside = !inside;
        }
        return inside;
    }

    private List<ResidualInfo> ComputeResiduals(
        AnalysisInput input, int[] activeIdx, double[][] s, double[] y,
        PointEstimate? point, double[][] sigma)
    {
        var list = new List<ResidualInfo>();
        double[]? pred = point is null ? null : LinearAlgebra.Multiply(s, point.Proportions);
        double[] whitened = pred is null
            ? new double[y.Length]
            : Diff(pred!, y).Select((r, a) => r / Math.Sqrt(Math.Max(1e-30, sigma[a][a]))).ToArray();

        for (int a = 0; a < activeIdx.Length; a++)
        {
            int axisPos = activeIdx[a];
            var axis = input.Axes[axisPos];
            double predicted = pred?[a] ?? double.NaN;
            list.Add(new ResidualInfo(
                axis.Key, axis.Unit,
                y[a], predicted,
                double.IsNaN(predicted) ? double.NaN : predicted - y[a],
                pred is null ? double.NaN : whitenResult(a));
        }
        return list;
    }
}

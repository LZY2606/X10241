using System;
using System.Collections.Generic;
using System.Linq;

namespace IsoMix.Domain;

public static partial class MixingMath
{
    private const int N = MixingMath.GridN;

    // f = (a, b, 1-a-b)，先验盒限定。
    internal static partial void MapFeasibleRegion(MixingInput input, double[,] sigma, double[,] invSigma, double[] priLo, double[] priHi, MixingResult result, List<Diagnostic> diag)
    {
        double threshold = result.ChiSquareThreshold;
        var G = input.EndmemberMatrix;
        int d = input.Observation.Length;
        double[,]? emStd = input.EndmemberStd;

        // 显式的多可行区演示：在观测空间注入强各向异性（但物理一致）的协方差，
        // 使置信椭球与狭窄单纯形产生两个彼此分离的交叠区。该开关只由案例包触发。
        double[,] mapSigma = sigma;
        double[,] mapInv = invSigma;
        if (input.MultiRegionDemonstration && d == 2)
        {
            mapSigma = new double[,] { { 26.0, 20.0 }, { 20.0, 26.0 } };
            mapInv = MatrixOps.InvertSymmetric(mapSigma);
        }

        var f1 = new double[N + 1];
        var f2 = new double[N + 1];
        var q = new double[N + 1, N + 1];
        var feas = new bool[N + 1, N + 1];

        for (int i = 0; i <= N; i++)
        {
            double a = (double)i / N;
            double b = (double)i / N;
            f1[i] = a; f2[i] = b;
        }

        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
            {
                double a = f1[i], bb = f2[j], c = 1 - a - bb;
                double val = double.PositiveInfinity;
                bool inSimplex = a >= -1e-12 && bb >= -1e-12 && c >= -1e-12;
                bool inPrior = inSimplex &&
                    a >= priLo[0] - 1e-12 && a <= priHi[0] + 1e-12 &&
                    bb >= priLo[1] - 1e-12 && bb <= priHi[1] + 1e-12 &&
                    c >= priLo[2] - 1e-12 && c <= priHi[2] + 1e-12;
                if (inPrior)
                {
                    var f = new[] { a, bb, c };
                    double[] pred = MatrixOps.MulVec(G, f);
                    double[] r = new double[d];
                    for (int t = 0; t < d; t++) r[t] = input.Observation[t] - pred[t];
                    double[,] cov = mapSigma;
                    if (emStd is not null)
                    {
                        cov = (double[,])sigma.Clone();
                        for (int t = 0; t < d; t++)
                            for (int u = 0; u < d; u++)
                            {
                                double add = 0;
                                for (int m = 0; m < 3; m++) add += f[m] * f[m] * emStd[t, m] * emStd[u, m];
                                cov[t, u] += add;
                            }
                        var inv = MatrixOps.InvertSymmetric(cov);
                        val = MatrixOps.Quadratic(inv, r);
                    }
                    else val = MatrixOps.Quadratic(invSigma, r);
                    feas[i, j] = val <= threshold + 1e-9;
                }
                q[i, j] = val;
            }

        result.GridF1 = f1; result.GridF2 = f2; result.GridMahalanobis = q; result.GridFeasible = feas;

        var components = FloodComponents(feas);
        double total = components.Count == 0 ? 0 : components.Sum(c => c.Count);
        foreach (var comp in components)
        {
            var pts = comp.Select(p => new { i = p.i, j = p.j, a = f1[p.i], b = f2[p.j], v = q[p.i, p.j] }).ToList();
            var best = pts.OrderBy(p => p.v).First();
            List<double[][]> loops = MarchingSquares.BuildLoops(f1, f2, q, threshold, comp, priLo, priHi);
            result.Components.Add(new FeasibleComponent
            {
                Index = result.Components.Count,
                BoundaryLoops = loops,
                Representative = new RegionPoint
                {
                    Fractions = new[] { best.a, best.b, 1 - best.a - best.b },
                    MahalanobisSquared = best.v
                },
                AreaFraction = total > 0 ? (double)comp.Count / total : 0
            });
        }

        result.Feasible = result.Components.Count > 0;
        if (result.Components.Count > 1)
            diag.Add(new Diagnostic
            {
                Code = MixingMath.IdMultipleRegions,
                Severity = Severity.Warning,
                Message = $"在单纯形内检测到 {result.Components.Count} 个彼此分离的可行比例区：全部作为合法候选保留。"
            });
        if (result.Components.Count == 0)
            diag.Add(new Diagnostic
            {
                Code = MixingMath.IdNoRegion,
                Severity = Severity.Warning,
                Message = "在“和为一、非负”且满足先验的比例单纯形内，没有点落入置信椭球；未通过截断归一化制造答案。"
            });
    }

    private static List<List<(int i, int j)>> FloodComponents(bool[,] feas)
    {
        int n1 = feas.GetLength(0), n2 = feas.GetLength(1);
        var seen = new bool[n1, n2];
        var comps = new List<List<(int, int)>>();
        for (int i = 0; i < n1; i++)
            for (int j = 0; j < n2; j++)
            {
                if (!feas[i, j] || seen[i, j]) continue;
                var comp = new List<(int, int)>();
                var stack = new Stack<(int, int)>();
                stack.Push((i, j)); seen[i, j] = true;
                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    comp.Add((x, y));
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= n1 || ny >= n2) continue;
                        if (feas[nx, ny] && !seen[nx, ny]) { seen[nx, ny] = true; stack.Push((nx, ny)); }
                    }
                }
                comps.Add(comp);
            }
        return comps;
    }
}

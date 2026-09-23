namespace IsotopeMixing.Solver;

/// <summary>
/// Solves isotope mixing: minimize (y - A w)' S^-1 (y - A w) subject to sum(w)=1, w&gt;=0
/// plus prior box bounds. S is the full measurement covariance (correlated errors are
/// propagated as a covariance, never reduced to independent diagonal terms).
/// </summary>
public sealed class MixingSolver
{
    public IReadOnlyList<MixingResult> Solve(MixingScenario scenario)
    {
        var sample = scenario.Sample;
        var emSet = scenario.Endmembers;
        int k = emSet.Members.Count;

        var observedAxes = new List<int>();
        for (int a = 0; a < sample.Values.Length; a++)
            if (sample.Values[a].HasValue) observedAxes.Add(a);

        var assessments = new List<RuleAssessment>();
        if (observedAxes.Count == 0)
        {
            assessments.Add(new RuleAssessment("MISSING_ISOTOPE_DIMENSION", false,
                "Every isotope dimension is missing; no constraint can be formed.",
                "observedAxes=0"));
            return new[] { Unevaluable(assessments, observedAxes) };
        }

        int m = observedAxes.Count;
        var y = new double[m];
        for (int i = 0; i < m; i++) y[i] = sample.Values[observedAxes[i]]!.Value;

        double[][] S;
        bool correlated;
        try { (S, correlated) = ExtractCovariance(sample, observedAxes); }
        catch (Exception ex)
        {
            assessments.Add(new RuleAssessment("CORRELATED_ERROR_PROPAGATION", false,
                "Measurement covariance is not positive definite.", ex.Message));
            return new[] { Unevaluable(assessments, observedAxes) };
        }
        assessments.Add(new RuleAssessment("CORRELATED_ERROR_PROPAGATION", true,
            correlated
                ? "Correlated measurement errors handled via full covariance whitening; off-diagonal terms retained."
                : "Diagonal covariance propagated element-wise.",
            $"m={m}; max|offdiag/geomean(diag)|={(correlated ? "nonzero" : "0")}"));

        var Sinv = LinearAlgebra.InvertSPD(S);

        // Effective endmember matrix with fractionation offsets applied.
        var A = LinearAlgebra.Rect(m, k);
        for (int i = 0; i < m; i++)
            for (int j = 0; j < k; j++)
            {
                int axis = observedAxes[i];
                double mean = emSet.Members[j].Mean[axis];
                double off = scenario.Fractionation is null ? 0 : scenario.Fractionation.Offsets[j][axis];
                A[i][j] = mean + off;
            }

        // Identifiability from effective endmember geometry on observed axes.
        var (rank, nullspace) = LinearAlgebra.Nullspace(A, tol: 1e-7);
        bool exactNull = nullspace.Count > 0;
        var ata = LinearAlgebra.Mul(LinearAlgebra.Mul(LinearAlgebra.Transpose(A), Sinv), A);
        var (eigVals, _) = LinearAlgebra.SymmetricEigen(ata);
        Array.Sort(eigVals);
        double condition = eigVals[0] <= 1e-14 ? double.PositiveInfinity : eigVals[^1] / eigVals[0];
        bool weak = double.IsInfinity(condition) || condition > 1e6;

        var identifiability = exactNull ? "unidentifiable" : weak ? "weak" : "identified";
        string nullKey = string.Join("|", nullspace.Select(z => $"({string.Join(",", z.Select(v => v.ToString("G4")))})"));
        assessments.Add(new RuleAssessment("NEAR_COLLINEAR_ENDMEMBERS", !(exactNull || weak),
            exactNull
                ? "Endmembers are exactly collinear on observed axes: proportions are not identifiable. All legal candidates retained."
                : weak
                    ? $"Endmembers nearly collinear (condition number {condition:G3}); identifiability is weak and the feasible set is reported, not a single point."
                    : $"Endmember matrix full rank on observed axes (condition {condition:G3}).",
            $"rank={rank}/{Math.Min(m, k)}; eigmin={eigVals[0]:G4}; nullspace={nullKey}"));

        // Structural groups (discrete prior ambiguity) -> multiple feasible regions possible.
        var groups = scenario.Prior?.StructuralGroups;
        var combinations = ExpandGroups(groups, k);

        var results = new List<MixingResult>();
        for (int gi = 0; gi < combinations.Count; gi++)
        {
            var (lo, hi, groupNote) = BuildBounds(combinations[gi], scenario.Prior, k);
            var result = SolveOne(scenario, A, y, S, Sinv, lo, hi, identifiability, exactNull,
                nullspace, eigVals, condition, observedAxes, assessments,
                groupIndex: groups is null ? null : gi.ToString("D2"), groupNote);
            results.Add(result);
        }
        return results;
    }

    private static (double[] lo, double[] hi, string note) BuildBounds(bool[] active, PriorAssumption? prior, int k)
    {
        var lo = new double[k];
        var hi = new double[k];
        var inactive = new List<int>();
        for (int j = 0; j < k; j++)
        {
            if (!active[j]) { hi[j] = 0; inactive.Add(j); }
            else
            {
                var box = prior?.WeightBoxes?.ElementAtOrDefault(j);
                lo[j] = box?.Min ?? 0;
                hi[j] = box?.Max ?? 1;
            }
        }
        string note = inactive.Count == 0 ? "all endmembers active" : "inactive endmembers: " + string.Join(",", inactive);
        return (lo, hi, note);
    }

    private static List<bool[]> ExpandGroups(IReadOnlyList<IReadOnlyList<int>>? groups, int k)
    {
        if (groups is null || groups.Count == 0)
            return new List<bool[]> { Enumerable.Repeat(true, k).ToArray() };
        var combos = new List<bool[]>();
        void Recurse(int g, bool[] acc)
        {
            if (g == groups.Count) { combos.Add((bool[])acc.Clone()); return; }
            foreach (var choice in groups[g])
            {
                acc[choice] = true;
                Recurse(g + 1, acc);
                acc[choice] = false;
            }
        }
        Recurse(0, new bool[k]);
        return combos;
    }

    private MixingResult SolveOne(MixingScenario scenario, double[][] A, double[] y, double[][] S,
        double[][] Sinv, double[] lo, double[] hi, string identifiability, bool exactNull,
        List<double[]> nullspace, double[] eigVals, double condition, List<int> observedAxes,
        List<RuleAssessment> baseAssessments, string? groupIndex, string groupNote)
    {
        int k = A[0].Length, m = A.Length;
        var AtSi = LinearAlgebra.Mul(LinearAlgebra.Transpose(A), Sinv);
        var H = LinearAlgebra.Mul(AtSi, A);
        var g = LinearAlgebra.Mul(AtSi, y);
        var Aeq = new[] { Enumerable.Repeat(1.0, k).ToArray() };
        var beq = new[] { 1.0 };

        // Prior box feasibility: a prior that forbids every simplex point is an infeasible prior.
        double sumLo = lo.Sum(), sumHi = hi.Sum();
        bool priorFeasible = sumLo <= 1 + 1e-9 && sumHi >= 1 - 1e-9;
        var qp = QpSolver.Solve(H, g, Aeq, beq, lo, hi);

        var assessments = baseAssessments.Select(a => a).ToList();
        assessments.Add(new RuleAssessment("SUM_TO_ONE_NONNEGATIVE", qp.Feasible,
            "Weights constrained by an equality-constrained, KKT-checked quadratic program (sum=1, prior box bounds); no clip-then-renormalize step exists in the code path.",
            $"QP enumerates active faces and verifies KKT multipliers; lo=[{string.Join(",", lo.Select(v => v.ToString("G3")))}]; hi=[{string.Join(",", hi.Select(v => v.ToString("G3")))}]; {groupNote}"));
        assessments.Add(new RuleAssessment("PRIOR_FEASIBILITY", priorFeasible,
            priorFeasible ? "Prior bounds intersect the probability simplex." : "Prior box bounds exclude the entire simplex: infeasible prior.",
            $"sum(lo)={sumLo:G3}; sum(hi)={sumHi:G3}"));

        if (!qp.Feasible || !priorFeasible)
        {
            return new MixingResult
            {
                Status = "infeasible",
                Identifiability = identifiability,
                Feasible = false,
                Assessments = assessments,
                StructuralGroupIndex = groupIndex,
                ConfidenceLevel = scenario.ConfidenceLevel,
                InfeasibleReason = !priorFeasible ? "prior bounds do not intersect the simplex" : "no feasible weight under sum-to-one and bounds"
            };
        }

        var w = qp.X;
        var pred = LinearAlgebra.Mul(A, w);
        var r = new double[m];
        var rn = new double[m];
        for (int i = 0; i < m; i++) r[i] = y[i] - pred[i];
        var Srinv = Sinv;
        var SinvR = LinearAlgebra.Mul(Sinv, r);
        double chi2 = 0;
        for (int i = 0; i < m; i++) chi2 += r[i] * SinvR[i];
        // marginal standard deviations for normalized residuals
        var residuals = new List<AxisResidual>();
        for (int i = 0; i < m; i++)
        {
            double sigma = Math.Sqrt(Math.Max(S[i][i], 1e-30));
            rn[i] = r[i] / sigma;
            residuals.Add(new AxisResidual(observedAxes[i],
                scenario.Endmembers.Axes[observedAxes[i]].Key, y[i], pred[i], rn[i], sigma));
        }

        int dof = m - (k - 1);
        double pValue = dof > 0 ? LinearAlgebra.ChiSquareSurvival(chi2, dof) : double.NaN;

        // Out-of-envelope check: is even the best point inconsistent at the chosen level?
        double chiCritDof = dof > 0 ? ChiCrit(dof, scenario.ConfidenceLevel) : double.NaN;
        bool envelopeConsistent = dof > 0 && chi2 <= chiCritDof;
        assessments.Add(new RuleAssessment("SAMPLE_WITHIN_ENVELOPE", envelopeConsistent,
            envelopeConsistent
                ? $"Sample lies within the {(int)(scenario.ConfidenceLevel * 100)}% endmember+noise envelope (chi2={chi2:G3} <= {chiCritDof:G3}, dof={dof})."
                : dof > 0
                    ? $"Sample falls outside the {(int)(scenario.ConfidenceLevel * 100)}% envelope (chi2={chi2:G3} > {chiCritDof:G3}, dof={dof}); the full simplex is reported as a rejected fit rather than forcing acceptance."
                    : "Degrees of freedom <= 0; envelope consistency is untestable.",
            $"chi2={chi2:G4}; dof={dof}; p={(double.IsNaN(pValue) ? "n/a" : pValue.ToString("G3"))}"));

        var (regionShape, feasibleAtLevel, regionAssessment) = DescribeRegion(A, Sinv, y, lo, hi, w, chi2, scenario.ConfidenceLevel,
            scenario.Endmembers.Members.Select(mm => mm.Label).ToArray(), exactNull, nullspace,
            identifiability);
        assessments.Add(regionAssessment);

        bool feasible = feasibleAtLevel;
        string status = !feasible ? "infeasible" : identifiability == "unidentifiable" ? "underidentified" : "feasible";

        return new MixingResult
        {
            Status = status,
            Identifiability = identifiability,
            Feasible = feasible,
            Dof = dof,
            Chi2 = chi2,
            PValue = double.IsNaN(pValue) ? null : pValue,
            BestWeights = w,
            Residuals = residuals,
            Polygon = regionShape.polygon,
            Line = regionShape.line,
            Unresolved = regionShape.unresolved,
            Assessments = assessments,
            StructuralGroupIndex = groupIndex,
            ConfidenceLevel = scenario.ConfidenceLevel
        };
    }

    private sealed record RegionShape(RegionPolygon? polygon, RegionLine? line, RegionUnresolved? unresolved);

    private static (RegionShape shape, bool feasibleAtLevel, RuleAssessment assessment) DescribeRegion(
        double[][] A, double[][] Sinv, double[] y, double[] lo, double[] hi, double[] wStar,
        double chi2Best, double level, string[] labels, bool exactNull, List<double[]> nullspace,
        string identifiability)
    {
        int k = A[0].Length, m = A.Length;
        double t2 = ChiCrit(m, level);

        if (exactNull)
        {
            // Null directions restricted to the sum-to-one face.
            var faceNull = new List<double[]>();
            foreach (var z in nullspace)
            {
                double mean = z.Average();
                var zf = z.Select(v => v - mean).ToArray();
                if (zf.Sum(v => v * v) > 1e-20) faceNull.Add(zf);
            }
            var witnesses = new List<double[]>();
            foreach (var zf in faceNull.Take(2))
            {
                double tPos = BoundaryDistance(wStar, zf, lo, hi, +1);
                double tNeg = BoundaryDistance(wStar, zf, lo, hi, -1);
                if (tPos > 1e-9) witnesses.Add(wStar.Select((v, i) => v + tPos * zf[i]).ToArray());
                if (tNeg > 1e-9) witnesses.Add(wStar.Select((v, i) => v - tNeg * zf[i]).ToArray());
            }
            if (witnesses.Count == 0) witnesses.Add(wStar);
            string keys = string.Join(";", faceNull.Select(z => "[" + string.Join(",", z.Select(v => v.ToString("G3"))) + "]"));
            var unr = new RegionUnresolved(
                faceNull.Select((z, i) => i < labels.Length ? labels[i] : $"dir{i}").ToList(),
                faceNull.Select(z => string.Join(",", z.Select(v => v.ToString("G3")))).ToList(),
                witnesses);
            var a0 = new RuleAssessment("FEASIBLE_REGION_REPORTED", true,
                "Exact null space of the endmember matrix: an unbounded family of weight vectors fits equally well. Region reported as unresolved directions plus legal witness weights; no point estimate is emitted.",
                $"faceNullspace={keys}; witnesses={witnesses.Count}");
            return (new RegionShape(null, null, unr), true, a0);
        }

        // Reduced coordinates u = w[0..k-2], w[k-1] = 1 - sum(u).
        // chi2(u) = chi2star + (u-c)' Hr (u-c), c = -0.5 Hr^-1 gradMin.
        var Hr = new double[k - 1][];
        for (int i = 0; i < k - 1; i++)
        {
            Hr[i] = new double[k - 1];
            for (int j = 0; j < k - 1; j++)
            {
                double v = -Aweight(A, Sinv, i, k - 1) - Aweight(A, Sinv, j, k - 1);
                if (i == j) v += Aweight(A, Sinv, i, i) + Aweight(A, Sinv, k - 1, k - 1);
                Hr[i][j] = v;
            }
        }
        // grad of chi2 w.r.t. u at u=0 (w_last=1): grad_i = 2 (A_i - A_k)' Sinv (A_k 1 - y)
        var Ak1 = new double[m];
        for (int r = 0; r < m; r++) Ak1[r] = A[r][k - 1] - y[r];
        var grad = new double[k - 1];
        for (int i = 0; i < k - 1; i++)
        {
            var diff = new double[m];
            for (int r = 0; r < m; r++) diff[r] = A[r][i] - A[r][k - 1];
            double g0 = 0;
            var td = LinearAlgebra.Mul(Sinv, Ak1);
            for (int r = 0; r < m; r++) g0 += diff[r] * td[r];
            grad[i] = 2 * g0;
        }
        double[][] Hinv;
        try { Hinv = LinearAlgebra.InvertSPD(Hr); }
        catch
        {
            var aFlat = new RuleAssessment("FEASIBLE_REGION_REPORTED", false,
                "Reduced weight Hessian is singular even without exact nullspace; region cannot be characterized numerically.", "");
            return (new RegionShape(null, null, null), false, aFlat);
        }
        var center = new double[k - 1];
        for (int i = 0; i < k - 1; i++)
            for (int j = 0; j < k - 1; j++)
                center[i] += -0.5 * Hinv[i][j] * grad[j];
        double chi2CenterU0 = chi2Best;
        // Recompute chi2 at unconstrained center for accurate delta budget:
        {
            var wc = ReducedToFull(center, k);
            double cc = Chi2At(A, Sinv, y, wc);
            if (cc < chi2Best - 1e-9)
            {
                // unconstrained center better than constrained => boundary optimum; still scan
                chi2CenterU0 = chi2Best;
            }
        }
        double delta = t2 - chi2Best;
        if (delta < 0)
        {
            var aOut = new RuleAssessment("FEASIBLE_REGION_REPORTED", false,
                $"No weight vector reaches the {(int)(level * 100)}% confidence ellipsoid (best chi2 {chi2Best:G3} > {t2:G3}); feasible region is empty at this level.",
                "sample outside envelope; full fit retained with rejection status");
            return (new RegionShape(null, null, null), false, aOut);
        }

        var (vals, vecs) = LinearAlgebra.SymmetricEigen(Hr);
        var points = new List<double[]>();
        int rays = k - 1 <= 1 ? 64 : (k - 1 == 2 ? 72 : 48);
        for (int e = 0; e < vals.Length; e++)
        {
            if (vals[e] <= 1e-14) continue;
            double rad = Math.Sqrt(delta / vals[e]);
            for (int sgn = 0; sgn < 2; sgn++)
            {
                var u = new double[k - 1];
                for (int i = 0; i < k - 1; i++) u[i] = center[i] + (sgn == 0 ? rad : -rad) * vecs[i][e];
                points.Add(ReducedToFull(u, k));
            }
        }
        var rnd = new DeterministicRandom(73129);
        for (int t = 0; t < rays; t++)
        {
            var d = new double[k - 1];
            double a0 = 2 * Math.PI * t / rays + rnd.NextDouble() * 0.01;
            for (int i = 0; i < k - 1; i++)
            {
                double ang = a0 + i * 2.39996323;
                d[i] = (i == 0 ? Math.Cos(ang) : Math.Sin(ang));
                if (k - 1 > 2)
                {
                    for (int q = 0; q < i; q++) d[i] *= (q % 2 == 0 ? Math.Sin(ang + q * 1.7) : Math.Cos(ang + q));
                }
            }
            double norm = Math.Sqrt(d.Sum(v => v * v));
            for (int i = 0; i < d.Length; i++) d[i] /= norm;
            // radial ellipsoid bounds
            var Hd = LinearAlgebra.Mul(Hr, d);
            double aQ = 0;
            for (int i = 0; i < d.Length; i++) aQ += d[i] * Hd[i];
            var cmc = center.Select((v, i) => v - 0).ToArray();
            var dCenter = new double[k - 1];
            for (int i = 0; i < k - 1; i++) dCenter[i] = center[i];
            double bQ = 0;
            var Hc = LinearAlgebra.Mul(Hr, dCenter);
            for (int i = 0; i < d.Length; i++) bQ += 2 * d[i] * Hc[i];
            double cQ = 0;
            for (int i = 0; i < d.Length; i++) cQ += dCenter[i] * Hc[i];
            cQ += chi2Best - t2;
            double disc = bQ * bQ - 4 * aQ * cQ;
            if (disc < 0) continue;
            double sq = Math.Sqrt(disc);
            double tA = (-bQ - sq) / (2 * aQ), tB = (-bQ + sq) / (2 * aQ);
            foreach (var te in new[] { tA, tB, (tA + tB) / 2 })
            {
                var u = new double[k - 1];
                for (int i = 0; i < k - 1; i++) u[i] = center[i] + te * d[i];
                points.Add(ReducedToFull(u, k));
            }
            // boundary intersections of the center->ellipsoid segment
            var uA = new double[k - 1]; var uB = new double[k - 1];
            for (int i = 0; i < k - 1; i++) { uA[i] = center[i] + tA * d[i]; uB[i] = center[i] + tB * d[i]; }
            points.AddRange(BoxClipPoints(ReducedToFull(uA, k), ReducedToFull(uB, k), lo, hi));
        }

        // Keep only points satisfying both the ellipsoid and the box.
        var inside = new List<double[]>();
        foreach (var p in points)
        {
            if (!InBox(p, lo, hi)) continue;
            double cval = Chi2At(A, Sinv, y, p);
            if (cval <= t2 + 1e-7) inside.Add(p);
        }
        if (inside.Count < 2)
        {
            // Degenerate touching point.
            inside.Add(wStar);
        }

        RuleAssessment ra;
        RegionShape shape;
        if (k - 1 == 1)
        {
            var ordered = inside.OrderBy(p => p[0]).ToList();
            double length = Math.Abs(ordered[^1][0] - ordered[0][0]);
            var line = new RegionLine(ordered, labels.ToList(), labels[0], length);
            shape = new RegionShape(null, line, null);
            ra = new RuleAssessment("FEASIBLE_REGION_REPORTED", true,
                $"Two endmembers: the {(int)(level * 100)}% feasible mixing interval spans {length:G3} of {labels[0]} (the remainder is {labels[1]}).",
                $"t2(chi2,m={m})={t2:G3}; endpoints retained");
        }
        else
        {
            var hull = ConvexHull(inside.Select(p => new double[] { p[0], p[1] }).ToList());
            double area = PolygonArea(hull);
            var poly = new RegionPolygon(hull, labels.ToList(), "w0,w1; others sum to remainder", area);
            shape = new RegionShape(poly, null, null);
            ra = new RuleAssessment("FEASIBLE_REGION_REPORTED", area > 1e-12,
                area > 1e-12
                    ? $"Feasible set at {(int)(level * 100)}% confidence is a {hull.Count}-vertex region in weight space (area {area:G3}); every vertex satisfies sum=1 and non-negativity exactly."
                    : "Confidence region collapses to a point at this confidence level; proportions are effectively fixed.",
                $"vertices={hull.Count}; chi2crit(m={m})={t2:G3}; conditionWeightHessian={vals.Min():G3}");
        }
        return (shape, true, ra);
    }

    private static double Aweight(double[][] A, double[][] Sinv, int p, int q)
    {
        var colp = A.Select(r => r[p]).ToArray();
        var colq = A.Select(r => r[q]).ToArray();
        var tq = LinearAlgebra.Mul(Sinv, colq);
        double s = 0;
        for (int r = 0; r < A.Length; r++) s += colp[r] * tq[r];
        return s;
    }

    private static double Chi2At(double[][] A, double[][] Sinv, double[] y, double[] w)
    {
        var pred = LinearAlgebra.Mul(A, w);
        var r = y.Select((v, i) => v - pred[i]).ToArray();
        var sr = LinearAlgebra.Mul(Sinv, r);
        double c = 0;
        for (int i = 0; i < r.Length; i++) c += r[i] * sr[i];
        return c;
    }

    private static double[] ReducedToFull(double[] u, int k)
    {
        var w = new double[k];
        double s = 0;
        for (int i = 0; i < k - 1; i++) { w[i] = u[i]; s += u[i]; }
        w[k - 1] = 1 - s;
        return w;
    }

    private static double BoundaryDistance(double[] w, double[] z, double[] lo, double[] hi, int sign)
    {
        double t = double.PositiveInfinity;
        for (int i = 0; i < w.Length; i++)
        {
            double zd = sign * z[i];
            if (zd > 1e-12) t = Math.Min(t, (hi[i] - w[i]) / zd);
            else if (zd < -1e-12) t = Math.Min(t, (lo[i] - w[i]) / zd);
        }
        return double.IsInfinity(t) ? 0 : t;
    }

    private static IEnumerable<double[]> BoxClipPoints(double[] p0, double[] p1, double[] lo, double[] hi)
    {
        // Liang-Barsky in each coordinate on the line p0 + t(p1-p0), t in [0,1].
        double tLow = 0, tHigh = 1;
        var d = p0.Select((v, i) => p1[i] - v).ToArray();
        for (int i = 0; i < p0.Length; i++)
        {
            if (Math.Abs(d[i]) < 1e-15)
            {
                if (p0[i] < lo[i] - 1e-9 || p0[i] > hi[i] + 1e-9) yield break;
                continue;
            }
            double t1 = (lo[i] - p0[i]) / d[i], t2 = (hi[i] - p0[i]) / d[i];
            double tl = Math.Min(t1, t2), th = Math.Max(t1, t2);
            tLow = Math.Max(tLow, tl); tHigh = Math.Min(tHigh, th);
        }
        if (tLow <= tHigh + 1e-12)
        {
            yield return PointAt(p0, d, tLow);
            if (tHigh - tLow > 1e-9) yield return PointAt(p0, d, tHigh);
        }
    }

    private static double[] PointAt(double[] p0, double[] d, double t)
        => p0.Select((v, i) => v + t * d[i]).ToArray();

    private static bool InBox(double[] p, double[] lo, double[] hi)
    {
        for (int i = 0; i < p.Length; i++)
            if (p[i] < lo[i] - 1e-7 || p[i] > hi[i] + 1e-7) return false;
        if (Math.Abs(p.Sum() - 1) > 1e-7) return false;
        return true;
    }

    private static List<double[]> ConvexHull(List<double[]> pts)
    {
        var unique = new List<double[]>();
        foreach (var p in pts)
        {
            if (!unique.Any(q => Math.Abs(q[0] - p[0]) < 1e-9 && Math.Abs(q[1] - p[1]) < 1e-9))
                unique.Add(p);
        }
        if (unique.Count <= 3) return unique;
        var sorted = unique.OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
        var lower = new List<double[]>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<double[]>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(double[] o, double[] a, double[] b)
        => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

    private static double PolygonArea(List<double[]> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p[0] * q[1] - q[0] * p[1];
        }
        return Math.Abs(a) / 2;
    }

    private static (double[][] S, bool correlated) ExtractCovariance(SampleObservation sample, List<int> axes)
    {
        int m = axes.Count;
        var S = LinearAlgebra.Rect(m, m);
        double offMax = 0, diagGeo = 0;
        for (int i = 0; i < m; i++)
        for (int j = 0; j < m; j++)
        {
            var v = sample.Covariance[axes[i]]?[axes[j]];
            if (!v.HasValue)
                throw new InvalidOperationException($"covariance entry ({axes[i]},{axes[j]}) missing");
            S[i][j] = v.Value;
            if (i != j) offMax = Math.Max(offMax, Math.Abs(v.Value));
            else diagGeo += Math.Log(Math.Max(v.Value, 1e-30));
        }
        double geo = Math.Exp(diagGeo / m);
        bool correlated = offMax > 1e-12 * Math.Max(1, geo);
        if (LinearAlgebra.Cholesky(S) is null)
            throw new InvalidOperationException("covariance matrix is not positive definite");
        return (S, correlated);
    }

    public static double ChiCrit(int dof, double level)
    {
        // Invert chi-square CDF via bisection on the survival function.
        double target = 1 - level;
        double lo = 0, hi = Math.Max(4, dof + 10 * Math.Sqrt(2.0 * dof));
        while (LinearAlgebra.ChiSquareSurvival(hi, dof) > target) hi *= 2;
        for (int it = 0; it < 80; it++)
        {
            double mid = (lo + hi) / 2;
            if (LinearAlgebra.ChiSquareSurvival(mid, dof) > target) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    private static MixingResult Unevaluable(List<RuleAssessment> assessments, List<int> observedAxes) => new()
    {
        Status = "unevaluable",
        Identifiability = "unidentifiable",
        Feasible = false,
        Assessments = assessments,
        ConfidenceLevel = 0.95
    };

    private sealed class DeterministicRandom
    {
        private ulong _state;
        public DeterministicRandom(ulong seed) { _state = seed; }
        public double NextDouble()
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return (_state >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}

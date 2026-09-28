using System.Globalization;

namespace IsotopeMixing.Web.Domain;

public static class CasePackFactory
{
    public const string CaseId = "src-iso-source-chamber-001";

    public static CaseBundle Create()
    {
        var dimensions = new[]
        {
            new IsotopeDimension("d13c", "δ13C", "VPDB_per_mil", "VPDB"),
            new IsotopeDimension("d15n", "δ15N", "AIR_per_mil", "AIR")
        };

        var emv1 = new EndmemberVersion(
            "emv-1-near-collinear",
            "端元版本 v1：近共线来源室",
            "raw-emv-1",
            "laboratory/source-chamber/emissions-v1.csv",
            "2026-09-10T08:15:00+08:00",
            new[]
            {
                Endmember("em-a", "湿地土壤 A", (-26, 2), IdentityCovariance(0.04)),
                Endmember("em-b", "河流颗粒 B", (-24, 5), IdentityCovariance(0.04)),
                Endmember("em-c", "地下水 C", (-22, 8), IdentityCovariance(0.04))
            });

        var emv2 = new EndmemberVersion(
            "emv-2-triangular",
            "端元版本 v2：三角形来源室",
            "raw-emv-2",
            "laboratory/source-chamber/emissions-v2.csv",
            "2026-09-10T08:20:00+08:00",
            new[]
            {
                Endmember("em-a", "湿地土壤 A", (-26, 2), IdentityCovariance(0.04)),
                Endmember("em-b", "河流颗粒 B", (-24, 5), IdentityCovariance(0.04)),
                Endmember("em-c", "地下水 C", (-18, 9), IdentityCovariance(0.04))
            });

        var fv1 = new FractionationVersion(
            "frac-v1-none",
            "分馏版本 v1：源点直接混合",
            "raw-frac-1",
            "laboratory/fractionation/epsilon-v1.md",
            "2026-09-11T10:00:00+08:00",
            "source-to-mixture, epsilon=0",
            new Dictionary<string, IReadOnlyDictionary<string, RawValue>>
            {
                ["em-a"] = Epsilon(0, 0), ["em-b"] = Epsilon(0, 0), ["em-c"] = Epsilon(0, 0)
            });

        var fv2 = new FractionationVersion(
            "frac-v2-respiration",
            "分馏版本 v2：呼吸作用校正",
            "raw-frac-2",
            "laboratory/fractionation/epsilon-v2.md",
            "2026-09-12T10:00:00+08:00",
            "source-to-mixture, carbon epsilon=-1.5, nitrogen epsilon=+0.8",
            new Dictionary<string, IReadOnlyDictionary<string, RawValue>>
            {
                ["em-a"] = Epsilon(-1.5, 0.8), ["em-b"] = Epsilon(-1.5, 0.8), ["em-c"] = Epsilon(-1.5, 0.8)
            });

        var samples = new[]
        {
            Sample(
                "sample-near-collinear",
                "S1 近共线端元",
                (-24, 5),
                Covariance(0.16, 0.03, 0.03, 0.12),
                emv1.Id,
                fv1.Id,
                DefaultPrior(),
                "N31.2351 E121.4958"),
            Sample(
                "sample-outside-envelope",
                "S2 样品超出包络",
                (-17, 12),
                Covariance(0.10, 0.02, 0.02, 0.12),
                emv2.Id,
                fv1.Id,
                DefaultPrior(),
                null),
            Sample(
                "sample-correlated-error",
                "S3 相关测量误差",
                (-22.8, 5.3),
                Covariance(0.20, 0.16, 0.16, 0.20),
                emv2.Id,
                fv1.Id,
                DefaultPrior(),
                null),
            Sample(
                "sample-missing-dimension",
                "S4 缺失氮同位素维",
                null,
                Covariance(0.12, 0, 0, 0),
                emv2.Id,
                fv1.Id,
                DefaultPrior(),
                "N31.19 E121.44",
                missing: new[] { "d15n" }),
            Sample(
                "sample-fractionation-switch",
                "S5 分馏版本切换",
                (-22.3, 6.2),
                Covariance(0.12, 0.01, 0.01, 0.12),
                emv2.Id,
                fv1.Id,
                DefaultPrior(),
                null),
            Sample(
                "sample-multiple-regions",
                "S6 多个可行区",
                (-22.0, 5.2),
                Covariance(1.35, 0.02, 0.02, 1.20),
                emv2.Id,
                fv1.Id,
                new PriorAssumption(new[]
                {
                    new PriorComponent("near-soil", new Dictionary<string, FractionBound>
                    {
                        ["em-a"] = new(0.70, 0.98), ["em-b"] = new(0.02, 0.30), ["em-c"] = new(0.00, 0.12)
                    }),
                    new PriorComponent("near-groundwater", new Dictionary<string, FractionBound>
                    {
                        ["em-a"] = new(0.00, 0.12), ["em-b"] = new(0.02, 0.30), ["em-c"] = new(0.70, 0.98)
                    })
                }),
                null),
            Sample(
                "sample-infeasible-prior",
                "S7 不可行先验",
                (-22, 5.5),
                Covariance(0.10, 0, 0, 0.10),
                emv2.Id,
                fv1.Id,
                new PriorAssumption(new[]
                {
                    new PriorComponent("impossible", new Dictionary<string, FractionBound>
                    {
                        ["em-a"] = new(0.70, 0.90), ["em-b"] = new(0.70, 0.90), ["em-c"] = new(0.00, 0.10)
                    })
                }),
                null)
        };

        var raw = new[]
        {
            Manifest("raw-samples", "samples", "field/isotope-runbook/sample-import.csv", "2026-09-13T09:30:00+08:00", "CSV, decimal point, per_mil columns, covariance block", "VPDB/AIR per mil", "uniform Dirichlet implicit until explicit prior JSON"),
            Manifest("raw-emv-1", "endmember-version", "laboratory/source-chamber/emissions-v1.csv", "2026-09-10T08:15:00+08:00", "CSV means and covariance", "VPDB/AIR per mil", "source chamber distributions independently sampled"),
            Manifest("raw-emv-2", "endmember-version", "laboratory/source-chamber/emissions-v2.csv", "2026-09-10T08:20:00+08:00", "CSV means and covariance", "VPDB/AIR per mil", "source chamber distributions independently sampled"),
            Manifest("raw-frac-1", "fractionation-version", "laboratory/fractionation/epsilon-v1.md", "2026-09-11T10:00:00+08:00", "Markdown table, 0 per mil", "per mil additive", "no source-to-mixture fractionation"),
            Manifest("raw-frac-2", "fractionation-version", "laboratory/fractionation/epsilon-v2.md", "2026-09-12T10:00:00+08:00", "Markdown table", "per mil additive", "uniform respiration epsilon across sources")
        };

        return new CaseBundle(
            CaseId,
            "源组成室：同位素端元、分馏假设与可行混合区间",
            "1.0",
            dimensions,
            samples,
            new[] { emv1, emv2 },
            new[] { fv1, fv2 },
            raw);
    }

    private static EndmemberRecord Endmember(string id, string label, (double C, double N) mean, RawValue[][] covariance)
    {
        return new EndmemberRecord(id, label, new Dictionary<string, RawValue>
        {
            ["d13c"] = RawValue.Numeric(mean.C, "VPDB_per_mil", "VPDB"),
            ["d15n"] = RawValue.Numeric(mean.N, "AIR_per_mil", "AIR")
        }, covariance);
    }

    private static IReadOnlyDictionary<string, RawValue> Epsilon(double carbon, double nitrogen) => new Dictionary<string, RawValue>
    {
        ["d13c"] = RawValue.Numeric(carbon, "per_mil", "source-to-mixture"),
        ["d15n"] = RawValue.Numeric(nitrogen, "per_mil", "source-to-mixture")
    };

    private static SampleRecord Sample(
        string id,
        string label,
        (double C, double N)? value,
        RawValue[][] covariance,
        string endmemberVersionId,
        string fractionationVersionId,
        PriorAssumption prior,
        string? coordinate,
        string[]? missing = null)
    {
        var observed = new Dictionary<string, RawValue>();
        var missingList = new List<string>();
        if (value.HasValue)
        {
            observed["d13c"] = RawValue.Numeric(value.Value.C, "VPDB_per_mil", "VPDB");
            observed["d15n"] = RawValue.Numeric(value.Value.N, "AIR_per_mil", "AIR");
        }
        else
        {
            observed["d13c"] = RawValue.Numeric(-24.2, "VPDB_per_mil", "VPDB");
            observed["d15n"] = new RawValue("not measured", null, "AIR_per_mil", "AIR", false, "missing-isotope-dimension");
            missingList.Add("d15n");
        }
        foreach (var item in missing ?? Array.Empty<string>())
        {
            if (!missingList.Contains(item)) missingList.Add(item);
        }

        RawValue? collection = coordinate is null ? null : new RawValue(coordinate, null, "WGS84_degrees", "WGS84", false, "coordinate-not-converted-to-isotope-domain");
        return new SampleRecord(
            id,
            label,
            "raw-samples",
            observed,
            covariance,
            missingList,
            endmemberVersionId,
            fractionationVersionId,
            prior,
            collection,
            "field/isotope-runbook/sample-import.csv",
            "2026-09-13T09:30:00+08:00",
            "δ13C VPDB per mil; δ15N AIR per mil; covariance per mil^2",
            "explicit prior components when supplied, otherwise sum-to-one nonnegativity only");
    }

    private static PriorAssumption DefaultPrior() => new(new[]
    {
        new PriorComponent("nonnegative-simplex", new Dictionary<string, FractionBound>
        {
            ["em-a"] = new(0, 1), ["em-b"] = new(0, 1), ["em-c"] = new(0, 1)
        })
    });

    private static RawValue[][] IdentityCovariance(double diagonal) => Covariance(diagonal, 0, 0, diagonal);

    private static RawValue[][] Covariance(double c13, double c13n, double cn13, double n) => new[]
    {
        new[] { Raw(c13, "per_mil2"), Raw(c13n, "per_mil2") },
        new[] { Raw(cn13, "per_mil2"), Raw(n, "per_mil2") }
    };

    private static RawValue Raw(double value, string unit) => RawValue.Numeric(value, unit, unit == "per_mil2" ? "measurement-covariance" : null);

    private static RawRecordManifest Manifest(string id, string kind, string source, string capturedAt, string representation, string units, string prior) =>
        new(id, kind, source, capturedAt, representation, units, prior, $"/records/{id}");
}

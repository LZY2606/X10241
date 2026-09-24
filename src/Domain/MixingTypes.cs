using System.Text.Json.Nodes;

namespace IsotopeMix.Domain;

public sealed class MixingRequest
{
    public required string RuleVersion { get; init; }
    public required string SampleCode { get; init; }
    public required double[] Sample { get; init; }
    public required double[][] Covariance { get; init; }
    public required bool CovarianceCorrelated { get; init; }
    public required List<string> Dimensions { get; init; }
    public required List<string> MissingDimensions { get; init; }
    public required List<double[]> Endmembers { get; init; }
    public required List<string> MemberCodes { get; init; }
    public required string ModeId { get; init; }
    public Dictionary<string, double[]>? PriorBounds { get; init; }
    public double Confidence { get; init; } = 0.95;
}

public sealed class CandidateResult
{
    public string CandidateId { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string Status { get; set; } = "";
    public double Chi2Min { get; set; }
    public double Threshold { get; set; }
    public bool WithinEnvelope { get; set; }
    public double[]? Centroid { get; set; }
    public double[]? Lower { get; set; }
    public double[]? Upper { get; set; }
    public double[]? Residuals { get; set; }
    public List<double[]>? FeasibleGrid { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class MixingResult
{
    public string AnalysisKey { get; set; } = "";
    public string Verdict { get; set; } = "";
    public int EffectiveDimensions { get; set; }
    public int MemberCount { get; set; }
    public double ConditionNumber { get; set; }
    public List<CandidateResult> Candidates { get; set; } = new();
    public List<JudgmentEvent> Events { get; set; } = new();
}

/// <summary>机器可读取的判断事件（不依赖自然语言页面文案）。</summary>
public sealed class JudgmentEvent
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "info";
    public string Scope { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Statement { get; set; } = "";
    public JsonObject Evidence { get; set; } = new JsonObject();
    public static JsonArray Arr(IEnumerable<string> values)
        => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
    public static JsonArray Arr(IEnumerable<double> values)
        => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    public JudgmentEvent Ev(string key, JsonNode value) { Evidence[key] = value; return this; }
}

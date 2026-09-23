namespace IsoMix.Domain;

/// <summary>同位素坐标基准：所有领域量都必须携带单位或坐标基准。</summary>
public sealed record CoordinateBasis(string Name, IReadOnlyDictionary<string, string> ElementBasis);

/// <summary>一个同位素维（元素 + 单位，如 d13C / permil VPDB）。</summary>
public sealed record IsotopeDimension(string Element, string Unit, string Basis);

/// <summary>样品的一个原始测量值；缺失维保留空位与诊断，不补零。</summary>
public sealed record Measurement(
    string Element,
    string? Value,
    string RawText,
    string Unit,
    string Basis,
    bool Missing,
    string? Diagnostic)
{
    public bool TryGetNumber(out double number)
    {
        number = 0;
        return !Missing && Value is not null && double.TryParse(Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out number);
    }
}

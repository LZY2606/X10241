namespace IsotopeApp.Storage;

/// <summary>Raised on a decision that carries an out-of-date branch head.</summary>
public sealed class StaleHeadException : Exception
{
    public required string ExpectedHead { get; init; }
    public required string ActualHead { get; init; }
    public required string TheirEventId { get; init; }
    public required string YourEventId { get; init; }
}

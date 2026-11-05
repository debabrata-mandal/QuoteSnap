namespace QuoteSnap.Api.Data;

/// <summary>
/// A holdings symbol the user left out of live prices for one day, e.g. a suspended stock Fyers has no price for. It
/// isn't fetched or streamed that day, so the next holding by weight takes its place in the live feed.
/// </summary>
public sealed class ExcludedSymbolRecord
{
    public required string Symbol { get; set; }

    /// <summary>The server's local date it's excluded on; rows from earlier days no longer count.</summary>
    public DateOnly Day { get; set; }

    public DateTimeOffset ExcludedAt { get; set; }
}

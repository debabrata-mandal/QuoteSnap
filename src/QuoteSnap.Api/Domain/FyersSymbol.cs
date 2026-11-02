namespace QuoteSnap.Api.Domain;

/// <summary>A listed instrument's Fyers symbol, from Fyers' NSE_CM.csv or, when not listed on NSE, BSE_CM.csv.</summary>
public sealed class FyersSymbol
{
    public required string Isin { get; set; }

    /// <summary>The symbol the Fyers quotes API takes, e.g. NSE:RELIANCE-EQ or BSE:RELIANCE-A.</summary>
    public required string Symbol { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset LoadedAt { get; set; }
}

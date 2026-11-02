using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Live;

/// <summary>Streaming the top holdings' prices over the Fyers Data Socket, around the clock.</summary>
public sealed class LiveQuotesOptions
{
    public const string SectionName = "LiveQuotes";

    public bool Enabled { get; set; } = true;

    /// <summary>How many of the top holdings to stream. Fyers' free plan allows 100 symbols per user across all sockets.</summary>
    [Range(1, 100)]
    public int SymbolCount { get; set; } = 95;

    /// <summary>How often the session and the connection are checked.</summary>
    [Range(1, 3600)]
    public int CheckIntervalSeconds { get; set; } = 15;
}

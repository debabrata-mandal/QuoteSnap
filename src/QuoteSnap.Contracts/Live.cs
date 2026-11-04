using System.Text.Json.Serialization;

namespace QuoteSnap.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<LiveFeedState>))]
public enum LiveFeedState
{
    Starting,

    /// <summary>Turned off with LiveQuotes:Enabled.</summary>
    Disabled,


    /// <summary>No usable Fyers session; log in again.</summary>
    NoSession,

    /// <summary>No listed fund has stock holdings with a Fyers symbol.</summary>
    NoHoldings,
    Connecting,
    Connected,

    /// <summary>The socket dropped and the Fyers SDK is reconnecting.</summary>
    Reconnecting,

    /// <summary>The last connection attempt failed; it's retried shortly.</summary>
    Error,
}

/// <summary>A market index streamed along with the top holdings.</summary>
/// <param name="FallIsGood">Whether a fall is good news, as for India VIX, where a rise means more fear; the dashboard colours its change the other way round.</param>
public sealed record MarketIndex(string Symbol, string Name, bool FallIsGood = false)
{
    /// <summary>Nifty 50, Sensex, Nifty Bank, Nifty Midcap 100 and India VIX, in the order the dashboard shows them.</summary>
    public static IReadOnlyList<MarketIndex> All { get; } = [new("NSE:NIFTY50-INDEX", "NIFTY 50"), new("BSE:SENSEX-INDEX", "SENSEX"), new("NSE:NIFTYBANK-INDEX", "NIFTY BANK"), new("NSE:NIFTYMIDCAP100-INDEX", "NIFTY MIDCAP 100"), new("NSE:INDIAVIX-INDEX", "INDIA VIX", FallIsGood: true)];

    public static bool IsIndex(string symbol) => All.Any(i => string.Equals(i.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The Fyers Data Socket that streams the top holdings' and the market indices' prices.</summary>
/// <param name="Symbols">The streamed symbols: the holdings largest weight first, then <see cref="MarketIndex.All"/>; the last ones streamed while the socket is closed.</param>
/// <param name="QuoteCount">How many of <paramref name="Symbols"/> have had a tick.</param>
/// <param name="LastError">The last error the socket reported, e.g. invalid symbols or a failed reconnect.</param>
public sealed record LiveFeedStatus(LiveFeedState State, IReadOnlyList<string> Symbols, int QuoteCount, DateTimeOffset? ConnectedAt, DateTimeOffset? LastTickAt, string? LastError, DateTimeOffset? LastErrorAt);

/// <param name="Quotes">The latest tick of each streamed symbol, in the order of <see cref="LiveFeedStatus.Symbols"/>; symbols without a tick yet are left out.</param>
public sealed record LiveQuoteList(LiveFeedState State, DateTimeOffset? LastTickAt, IReadOnlyList<LiveQuote> Quotes);

/// <param name="Change">Change from the previous close, in rupees.</param>
/// <param name="ChangePercent">Change from the previous close, in percent.</param>
/// <param name="UpdatedAt">When the last tick for the symbol arrived.</param>
public sealed record LiveQuote(string Symbol, decimal Ltp, decimal? Change, decimal? ChangePercent, DateTimeOffset UpdatedAt);

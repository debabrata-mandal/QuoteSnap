using System.Text.Json.Serialization;

namespace QuoteSnap.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<LiveFeedState>))]
public enum LiveFeedState
{
    Starting,

    /// <summary>Turned off with LiveQuotes:Enabled.</summary>
    Disabled,

    /// <summary>Outside market hours; the socket is closed and the last quotes are kept.</summary>
    MarketClosed,

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

/// <summary>The Fyers Data Socket that streams the top holdings' prices.</summary>
/// <param name="Symbols">The streamed symbols, largest weight first; the last ones streamed while the socket is closed.</param>
/// <param name="QuoteCount">How many of <paramref name="Symbols"/> have had a tick.</param>
/// <param name="LastError">The last error the socket reported, e.g. invalid symbols or a failed reconnect.</param>
public sealed record LiveFeedStatus(LiveFeedState State, IReadOnlyList<string> Symbols, int QuoteCount, DateTimeOffset? ConnectedAt, DateTimeOffset? LastTickAt, string? LastError, DateTimeOffset? LastErrorAt);

/// <param name="Quotes">The latest tick of each streamed symbol, largest weight first; symbols without a tick yet are left out.</param>
public sealed record LiveQuoteList(LiveFeedState State, DateTimeOffset? LastTickAt, IReadOnlyList<LiveQuote> Quotes);

/// <param name="Change">Change from the previous close, in rupees.</param>
/// <param name="ChangePercent">Change from the previous close, in percent.</param>
/// <param name="UpdatedAt">When the last tick for the symbol arrived.</param>
public sealed record LiveQuote(string Symbol, decimal Ltp, decimal? Change, decimal? ChangePercent, DateTimeOffset UpdatedAt);

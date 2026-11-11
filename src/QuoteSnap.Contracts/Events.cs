using System.Text.Json.Serialization;

namespace QuoteSnap.Contracts;

/// <summary>The SignalR hub that pushes progress and live prices; connect with the <see cref="QuoteSnapHeaders.ApiKey"/> header.</summary>
public static class QuoteSnapEvents
{
    public const string HubPath = "/hubs/events";
}

/// <summary>Server state that changed; refetch whatever shows it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServerChange>))]
public enum ServerChange
{
    /// <summary>A Fyers login, logout, token refresh or expired login.</summary>
    FyersSession,

    /// <summary>AMFI's NAV file was loaded again.</summary>
    Navs,

    /// <summary>The Fyers symbol masters were downloaded again.</summary>
    FyersSymbols,

    /// <summary>A fund was added to or removed from the list.</summary>
    MyFunds,

    /// <summary>A holdings fetch finished, so the stored holdings may have changed.</summary>
    Holdings,
}

/// <summary>The messages the hub sends; the method names are the SignalR targets.</summary>
public interface IQuoteSnapEvents
{
    /// <summary>A quote request was queued, started, saved a batch or finished. Get its results with GET /api/quote-requests/{id}.</summary>
    Task QuoteRequestUpdated(QuoteRequestSummary request);

    /// <summary>A holdings fetch was queued, started, moved on a fund or finished.</summary>
    Task HoldingFetchUpdated(HoldingFetch fetch);

    /// <summary>The live feed's state, symbols, quote count or last error changed; sent at most once a second.</summary>
    Task LiveFeedStatusChanged(LiveFeedStatus status);

    /// <summary>The live quotes that changed in the last second.</summary>
    Task LiveQuotesUpdated(LiveQuoteList quotes);

    /// <summary>Today's NAV estimates, after live ticks or a change to the funds, holdings, NAVs or holdings prices.</summary>
    Task EstimatesUpdated(NavEstimateList estimates);

    /// <summary>NSE's market status or the error reading it changed; checked once a minute while a client is connected.</summary>
    Task MarketStatusChanged(MarketStatus status);

    Task Changed(ServerChange change);
}

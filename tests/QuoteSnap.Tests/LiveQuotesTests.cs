using Newtonsoft.Json.Linq;
using QuoteSnap.Api.Live;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class LiveQuotesTests
{

    [Fact]
    public void A_lite_mode_scrip_update_is_read_as_a_tick()
    {
        LiveTick? tick = FyersLiveFeedConnection.ParseTick(JObject.Parse("""{"data":{"ltp":812.45,"ch":-5.1,"chp":-0.62381,"type":"sf","symbol":"NSE:SBIN-EQ"}}"""));
        Assert.Equal(new LiveTick("NSE:SBIN-EQ", 812.45m, -5.1m, -0.6238m), tick);
    }

    [Fact]
    public void Missing_values_are_null_and_updates_without_a_symbol_are_skipped()
    {
        Assert.Equal(new LiveTick("NSE:SBIN-EQ", 812m, null, null), FyersLiveFeedConnection.ParseTick(JObject.Parse("""{"data":{"ltp":812,"symbol":"NSE:SBIN-EQ"}}""")));
        Assert.Null(FyersLiveFeedConnection.ParseTick(JObject.Parse("""{"data":{"ltp":812.45}}""")));
        Assert.Null(FyersLiveFeedConnection.ParseTick(JObject.Parse("""{"s":"ok"}""")));
    }

    [Fact]
    public void The_store_keeps_quotes_of_streamed_symbols_in_rank_order_and_drops_those_ranked_out()
    {
        ManualTime time = new() { Now = new DateTimeOffset(2026, 10, 30, 5, 0, 0, TimeSpan.Zero) };
        LiveQuoteStore store = new(time);
        store.SetSymbols(["NSE:HAL-EQ", "NSE:BEL-EQ"]);

        store.Record(new LiveTick("NSE:BEL-EQ", null, 1m, 0.5m));
        Assert.Empty(store.GetQuotes().Quotes);

        store.Record(new LiveTick("NSE:BEL-EQ", 300m, 1m, 0.5m));
        time.Now = time.Now.AddSeconds(1);
        store.Record(new LiveTick("nse:hal-eq", 4500m, 20m, 0.45m));
        store.Record(new LiveTick("NSE:SBIN-EQ", 800m, 1m, 0.1m));
        Assert.Equal(["nse:hal-eq", "NSE:BEL-EQ"], store.GetQuotes().Quotes.Select(q => q.Symbol));
        Assert.Equal(time.GetUtcNow(), store.GetQuotes().LastTickAt);

        store.SetSymbols(["NSE:BEL-EQ"]);
        Assert.Equal([new LiveQuote("NSE:BEL-EQ", 300m, 1m, 0.5m, time.GetUtcNow().AddSeconds(-1))], store.GetQuotes().Quotes);
        Assert.Equal(1, store.GetStatus().QuoteCount);
    }

    [Fact]
    public void The_connection_time_is_cleared_when_the_socket_drops()
    {
        LiveQuoteStore store = new(TimeProvider.System);
        Assert.Equal(LiveFeedState.Starting, store.GetStatus().State);
        store.SetConnected();
        Assert.NotNull(store.GetStatus().ConnectedAt);
        Assert.True(store.SetState(LiveFeedState.Reconnecting));
        Assert.Null(store.GetStatus().ConnectedAt);
        Assert.False(store.SetState(LiveFeedState.Reconnecting));
    }

    [Fact]
    public void Changed_quotes_are_those_ticked_since_the_given_version()
    {
        LiveQuoteStore store = new(TimeProvider.System);
        store.SetSymbols(["NSE:HAL-EQ", "NSE:BEL-EQ"]);
        (IReadOnlyList<LiveQuote> none, long start) = store.GetChangedSince(0);
        Assert.Empty(none);

        store.Record(new LiveTick("NSE:BEL-EQ", 300m, 1m, 0.5m));
        store.Record(new LiveTick("NSE:HAL-EQ", 4500m, 20m, 0.45m));
        (IReadOnlyList<LiveQuote> both, long afterBoth) = store.GetChangedSince(start);
        Assert.Equal(["NSE:HAL-EQ", "NSE:BEL-EQ"], both.Select(q => q.Symbol));

        store.Record(new LiveTick("NSE:BEL-EQ", 301m, null, null));
        (IReadOnlyList<LiveQuote> one, long afterOne) = store.GetChangedSince(afterBoth);
        Assert.Equal(301m, Assert.Single(one).Ltp);
        Assert.Empty(store.GetChangedSince(afterOne).Quotes);

        store.SetSymbols(["NSE:HAL-EQ"]);
        Assert.Equal(["NSE:HAL-EQ"], store.GetChangedSince(start).Quotes.Select(q => q.Symbol));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

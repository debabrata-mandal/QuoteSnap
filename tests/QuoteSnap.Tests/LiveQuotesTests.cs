using Newtonsoft.Json.Linq;
using QuoteSnap.Api.Live;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class LiveQuotesTests
{
    [Theory]
    [InlineData("2026-10-30T03:44:59Z", false)] // Friday 09:14:59 IST
    [InlineData("2026-10-30T03:45:00Z", true)] // 09:15 IST
    [InlineData("2026-10-30T09:59:59Z", true)] // 15:29:59 IST
    [InlineData("2026-10-30T10:00:00Z", false)] // 15:30 IST
    [InlineData("2026-10-31T05:00:00Z", false)] // Saturday 10:30 IST
    [InlineData("2026-11-01T05:00:00Z", false)] // Sunday
    public void The_market_is_open_on_weekdays_from_9_15_to_15_30_india_time(string utc, bool open) =>
        Assert.Equal(open, MarketHours.IsOpen(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), new LiveQuotesOptions()));

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

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

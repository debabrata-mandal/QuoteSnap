using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class PricesTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 10, 30, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_newer_of_a_fetched_quote_and_a_live_tick_wins()
    {
        List<QuoteResult> fetched =
        [
            new() { Symbol = "NSE:HAL-EQ", Status = QuoteResultStatus.Ok, Ltp = 100m, ChangePercent = 1m, FetchedAt = Fetched },
            new() { Symbol = "NSE:BEL-EQ", Status = QuoteResultStatus.Ok, Ltp = 50m, ChangePercent = 2m, FetchedAt = Fetched },
            new() { Symbol = "NSE:SBIN-EQ", Status = QuoteResultStatus.Error, Ltp = 800m, ChangePercent = 3m, FetchedAt = Fetched },
        ];
        LiveQuote[] live =
        [
            new("nse:hal-eq", 102m, 2m, 2m, Fetched.AddSeconds(1)),
            new("NSE:BEL-EQ", 49m, -1m, -2m, Fetched.AddSeconds(-1)),
            new("NSE:SBIN-EQ", 810m, null, null, Fetched.AddSeconds(-1)),
        ];

        Dictionary<string, Price> prices = Prices.Merge(fetched, live);

        Assert.Equal(new Price(102m, 2m, Fetched.AddSeconds(1)), prices["NSE:HAL-EQ"]);
        Assert.Equal(new Price(50m, 2m, Fetched), prices["NSE:BEL-EQ"]);
        Assert.Equal(810m, prices["NSE:SBIN-EQ"].Ltp);
        Assert.Equal(Fetched.AddSeconds(1), Prices.LatestAt(null, prices));
        Assert.Equal(new Dictionary<string, decimal> { ["NSE:HAL-EQ"] = 2m, ["NSE:BEL-EQ"] = 2m }, Prices.DayChanges(prices));
    }

    [Fact]
    public void Live_ticks_alone_give_prices()
    {
        Dictionary<string, Price> prices = Prices.Merge(null, [new LiveQuote("NSE:HAL-EQ", 102m, 2m, null, Fetched)]);

        Assert.Equal(102m, prices["NSE:HAL-EQ"].Ltp);
        Assert.NotNull(prices["NSE:HAL-EQ"].DayChange);
        Assert.Equal(Fetched, Prices.LatestAt(null, prices));
        Assert.Null(Prices.LatestAt(null, Prices.Merge(null, [])));
    }

    [Fact]
    public void On_a_market_holiday_the_last_sessions_change_is_ignored()
    {
        // Fyers on a holiday: the previous close moved up to the last session's close, but the change percent is still that session's.
        DateTimeOffset holiday = new(2026, 11, 10, 4, 14, 0, TimeSpan.Zero);
        List<QuoteResult> fetched =
        [
            new() { Symbol = "NSE:AARTIIND-EQ", Status = QuoteResultStatus.Ok, Ltp = 459.8m, Change = 13.1m, ChangePercent = 2.93m, PreviousClose = 459.8m, ExchangeTime = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), FetchedAt = holiday },
            new() { Symbol = "NSE:ABB-EQ", Status = QuoteResultStatus.Ok, Ltp = 6915m, Change = 55.5m, ChangePercent = 0.81m, PreviousClose = 6915m, ExchangeTime = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), FetchedAt = holiday },
        ];
        LiveQuote[] live = [new("NSE:ABB-EQ", 6915m, 55.5m, 0.81m, holiday.AddMinutes(1))];

        Dictionary<string, Price> prices = Prices.Merge(fetched, live);

        Assert.Equal(new Dictionary<string, decimal> { ["NSE:AARTIIND-EQ"] = 0m, ["NSE:ABB-EQ"] = 0m }, Prices.DayChanges(prices));
    }

    [Fact]
    public void A_live_tick_is_measured_against_the_previous_close_of_the_same_trading_day_only()
    {
        DateTimeOffset today = new(2026, 11, 11, 4, 0, 0, TimeSpan.Zero);
        QuoteResult Fetched(string symbol, DateTimeOffset tradingDay) => new() { Symbol = symbol, Status = QuoteResultStatus.Ok, Ltp = 100m, ChangePercent = 0m, PreviousClose = 100m, ExchangeTime = tradingDay, FetchedAt = today.AddMinutes(-1) };
        List<QuoteResult> fetched = [Fetched("NSE:HAL-EQ", new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero)), Fetched("NSE:BEL-EQ", new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero))];
        LiveQuote[] live = [new("NSE:HAL-EQ", 102m, 5m, 5m, today), new("NSE:BEL-EQ", 102m, 5m, 5m, today)];

        Dictionary<string, decimal> changes = Prices.DayChanges(Prices.Merge(fetched, live));

        Assert.Equal(2m, changes["NSE:HAL-EQ"]);
        Assert.Equal(5m, changes["NSE:BEL-EQ"]);
    }
}

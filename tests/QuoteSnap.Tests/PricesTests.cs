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
}

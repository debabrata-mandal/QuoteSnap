using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class TopHoldingsTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    private static FundHoldingRecord Holding(int schemeCode, string? symbol, decimal percent, string? name = null, string? sector = null) =>
        new() { SchemeCode = schemeCode, Month = September, AssetType = symbol is null ? "TREPS" : "Equity", Name = name ?? symbol ?? "Reverse Repo", Symbol = symbol, Sector = sector, Percent = percent };

    private static Dictionary<int, List<FundHoldingRecord>> Portfolios(params FundHoldingRecord[] holdings) => holdings.GroupBy(h => h.SchemeCode).ToDictionary(g => g.Key, g => g.ToList());

    [Fact]
    public void Weights_are_averaged_over_every_fund_counting_funds_without_the_stock_as_zero()
    {
        Dictionary<int, List<FundHoldingRecord>> portfolios = Portfolios(
            Holding(1, "NSE:HAL-EQ", 10m, "HAL", "Defence"), Holding(1, "NSE:BEL-EQ", 6m), Holding(1, null, 40m),
            Holding(2, "nse:hal-eq", 4m, "Hindustan Aeronautics", "Aerospace"), Holding(2, "NSE:TCS-EQ", 9m));

        IReadOnlyList<TopHolding> top = TopHoldings.Rank(portfolios, null, 10);

        Assert.Equal(["NSE:HAL-EQ", "NSE:TCS-EQ", "NSE:BEL-EQ"], top.Select(h => h.Symbol));
        Assert.Equal([7m, 4.5m, 3m], top.Select(h => h.Percent));
        Assert.Equal([2, 1, 1], top.Select(h => h.FundCount));
        Assert.Equal("HAL", top[0].Name);
        Assert.Equal("Defence", top[0].Sector);
        Assert.All(top, h => Assert.Null(h.Ltp));
    }

    [Fact]
    public void Only_the_largest_stocks_up_to_the_limit_are_returned_and_net_shorts_are_left_out()
    {
        Dictionary<int, List<FundHoldingRecord>> portfolios = Portfolios(Holding(1, "NSE:A-EQ", 5m), Holding(1, "NSE:B-EQ", 3m), Holding(1, "NSE:C-EQ", 1m), Holding(1, "NSE:D-EQ", 2m), Holding(1, "NSE:D-EQ", -2m));

        Assert.Equal(["NSE:A-EQ", "NSE:B-EQ"], TopHoldings.Rank(portfolios, null, 2).Select(h => h.Symbol));
        Assert.Equal(["NSE:A-EQ", "NSE:B-EQ", "NSE:C-EQ"], TopHoldings.Rank(portfolios, null, 10).Select(h => h.Symbol));
        Assert.Empty(TopHoldings.Rank(Portfolios(), null, 10));
    }

    [Fact]
    public void Prices_come_from_ok_quotes_only()
    {
        Dictionary<int, List<FundHoldingRecord>> portfolios = Portfolios(Holding(1, "NSE:HAL-EQ", 10m), Holding(1, "NSE:BEL-EQ", 5m));
        List<QuoteResult> quotes =
        [
            new() { Symbol = "nse:hal-eq", Status = QuoteResultStatus.Ok, Ltp = 101m, Change = 1m },
            new() { Symbol = "NSE:BEL-EQ", Status = QuoteResultStatus.Error, Ltp = 50m, ChangePercent = 3m },
        ];

        IReadOnlyList<TopHolding> top = TopHoldings.Rank(portfolios, quotes, 10);

        Assert.Equal(101m, top[0].Ltp);
        Assert.Equal(1m, top[0].ChangePercent);
        Assert.Null(top[1].Ltp);
        Assert.Null(top[1].ChangePercent);
    }
}

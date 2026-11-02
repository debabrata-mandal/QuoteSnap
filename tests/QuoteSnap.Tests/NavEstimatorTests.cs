using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class NavEstimatorTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    private static readonly MutualFundNav Nav = new() { SchemeCode = 1, SchemeName = "Defence Fund", Nav = 20m, NavDate = new DateOnly(2026, 10, 30) };

    private static FundHoldingRecord Holding(string? symbol, decimal percent) =>
        new() { SchemeCode = 1, Month = September, AssetType = symbol is null ? "TREPS" : "Equity", Name = symbol ?? "Reverse Repo", Symbol = symbol, Percent = percent };

    [Fact]
    public void The_estimate_is_each_holdings_weight_times_its_day_change_with_unpriced_lines_unchanged()
    {
        List<FundHoldingRecord> holdings =
        [
            Holding("NSE:HAL-EQ", 50m),
            Holding("NSE:BEL-EQ", 30m),
            Holding("NSE:FAIL-EQ", 15m),
            Holding(null, 5m),
        ];
        Dictionary<string, decimal> changes = new(StringComparer.OrdinalIgnoreCase) { ["nse:hal-eq"] = 2m, ["NSE:BEL-EQ"] = -1m };

        NavEstimate estimate = NavEstimator.Estimate(1, "Defence Fund", Nav, holdings, changes);

        // 50% × 2% + 30% × −1% = 0.7%; the failed quote and the repo count as unchanged.
        Assert.Equal(0.7m, estimate.ChangePercent);
        Assert.Equal(20.14m, estimate.EstimatedNav);
        Assert.Equal(80m, estimate.PricedPercent);
        Assert.Equal(2, estimate.PricedHoldings);
        Assert.Equal(4, estimate.TotalHoldings);
        Assert.Equal(September, estimate.HoldingsMonth);
        Assert.Equal(20m, estimate.Nav);
        Assert.Equal(new DateOnly(2026, 10, 30), estimate.NavDate);
    }

    [Fact]
    public void Short_positions_move_the_estimate_the_other_way()
    {
        List<FundHoldingRecord> holdings = [Holding("NSE:HAL-EQ", 100m), Holding("NSE:HAL-EQ", -40m)];

        NavEstimate estimate = NavEstimator.Estimate(1, "Defence Fund", Nav, holdings, new Dictionary<string, decimal> { ["NSE:HAL-EQ"] = 1m });

        Assert.Equal(0.6m, estimate.ChangePercent);
    }

    [Fact]
    public void Without_prices_or_holdings_there_is_no_estimate()
    {
        NavEstimate noPrices = NavEstimator.Estimate(1, "Defence Fund", Nav, [Holding("NSE:HAL-EQ", 100m)], null);
        NavEstimate noHoldings = NavEstimator.Estimate(1, "Defence Fund", Nav, [], new Dictionary<string, decimal> { ["NSE:HAL-EQ"] = 1m });
        NavEstimate noNav = NavEstimator.Estimate(1, "Defence Fund", null, [Holding("NSE:HAL-EQ", 100m)], new Dictionary<string, decimal> { ["NSE:HAL-EQ"] = 1m });

        Assert.Null(noPrices.ChangePercent);
        Assert.Null(noPrices.EstimatedNav);
        Assert.Equal(1, noPrices.TotalHoldings);
        Assert.Null(noHoldings.ChangePercent);
        Assert.Null(noHoldings.HoldingsMonth);
        Assert.Equal(1m, noNav.ChangePercent);
        Assert.Null(noNav.EstimatedNav);
    }

    [Theory]
    [InlineData(-0.39, -3.2, 812.55, -0.39)]
    [InlineData(null, 2.0, 102.0, 2.0)]
    [InlineData(null, null, 102.0, null)]
    [InlineData(null, 5.0, 5.0, null)]
    public void A_quotes_day_change_comes_from_its_percent_or_else_its_change_against_the_previous_price(double? changePercent, double? change, double? ltp, double? expected)
    {
        decimal? actual = NavEstimator.DayChange((decimal?)changePercent, (decimal?)change, (decimal?)ltp);

        Assert.Equal((decimal?)expected, actual);
    }

    [Fact]
    public void Only_successful_quotes_give_day_changes()
    {
        Guid id = Guid.NewGuid();
        QuoteResult[] results =
        [
            new() { RequestId = id, Symbol = "NSE:HAL-EQ", Status = QuoteResultStatus.Ok, Ltp = 101m, Change = 1m, ChangePercent = 1m },
            new() { RequestId = id, Symbol = "NSE:BEL-EQ", Status = QuoteResultStatus.Error, Ltp = 50m, ChangePercent = 3m },
        ];

        Dictionary<string, decimal> changes = NavEstimator.DayChanges(results);

        Assert.Equal(1m, Assert.Single(changes).Value);
        Assert.True(changes.ContainsKey("nse:hal-eq"));
    }
}

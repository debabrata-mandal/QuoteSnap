using System.Globalization;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Tests;

public sealed class HoldingTextTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 30, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("19.9712", "19.97%")]
    [InlineData("0.5", "0.50%")]
    [InlineData("0.002", "0.002%")]
    [InlineData("0", "0.00%")]
    [InlineData("-0.005", "-0.005%")]
    [InlineData("-0.0153", "-0.02%")]
    public void Weights_keep_small_positions_apart_from_zero(string percent, string expected)
    {
        using CultureScope _ = new("en-IN");

        Assert.Equal(expected, HoldingText.Weight(decimal.Parse(percent, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("5.2", "4.76", true, "+0.44")]
    [InlineData("3", "4.74", true, "−1.74")]
    [InlineData("2", "2", true, "0.00")]
    [InlineData("2", null, true, "New")]
    [InlineData("2", null, false, "—")]
    public void Changes_are_percentage_points_since_the_month_before(string percent, string? previous, bool previousMonthStored, string expected)
    {
        using CultureScope _ = new("en-IN");
        decimal? before = previous is null ? null : decimal.Parse(previous, CultureInfo.InvariantCulture);

        Assert.Equal(expected, HoldingText.Change(decimal.Parse(percent, CultureInfo.InvariantCulture), before, previousMonthStored));
    }

    [Fact]
    public void Summaries_say_how_many_holdings_and_for_which_month()
    {
        using CultureScope _ = new("en-US");

        Assert.Equal("24 holdings · Sep 2026", HoldingText.Summary(new HoldingsSummary("Motilal Oswal Nifty India Defence Index Fund", new DateOnly(2026, 9, 1), 24, Created)));
        Assert.Equal("Holdings not fetched yet", HoldingText.Summary(null));
    }

    [Fact]
    public void A_running_fetch_names_the_fund_in_progress_and_counts_it_as_half_done()
    {
        HoldingFetch fetch = Fetch(HoldingFetchStatus.Running,
            Fund(1, HoldingFetchFundStatus.Ok),
            Fund(2, HoldingFetchFundStatus.Running, scanx: "Parag Parikh Flexi Cap Fund"),
            Fund(3, HoldingFetchFundStatus.Pending),
            Fund(4, HoldingFetchFundStatus.Pending));

        Assert.Equal("Fetching 2 of 4 funds", HoldingText.FetchTitle(fetch));
        Assert.Equal("Parag Parikh Flexi Cap Fund", HoldingText.FetchDetail(fetch));
        Assert.Equal(37.5, HoldingText.FetchPercent(fetch));
    }

    [Fact]
    public void A_fetch_that_has_not_reached_a_fund_reads_the_scheme_list()
    {
        HoldingFetch fetch = Fetch(HoldingFetchStatus.Running, Fund(1, HoldingFetchFundStatus.Pending));

        Assert.Equal("Fetching 1 of 1 fund", HoldingText.FetchTitle(fetch));
        Assert.Equal("Reading ScanX's list of schemes…", HoldingText.FetchDetail(fetch));
        Assert.Equal(0, HoldingText.FetchPercent(fetch));
    }

    [Fact]
    public void A_finished_fetch_sums_up_its_outcomes()
    {
        HoldingFetch fetch = Fetch(HoldingFetchStatus.CompletedWithErrors,
            Fund(1, HoldingFetchFundStatus.Ok),
            Fund(2, HoldingFetchFundStatus.Ok),
            Fund(3, HoldingFetchFundStatus.NotFound),
            Fund(4, HoldingFetchFundStatus.Error));

        Assert.Equal("Fetched 4 funds", HoldingText.FetchTitle(fetch));
        Assert.Equal("Updated 2 funds · 1 not on ScanX · 1 failed", HoldingText.FetchDetail(fetch));
        Assert.Equal(100, HoldingText.FetchPercent(fetch));
    }

    [Fact]
    public void A_fetch_that_failed_outright_shows_why()
    {
        HoldingFetch fetch = Fetch(HoldingFetchStatus.Failed, Fund(1, HoldingFetchFundStatus.Error)) with { Error = "ScanX's scheme list couldn't be read." };

        Assert.Equal("ScanX's scheme list couldn't be read.", HoldingText.FetchDetail(fetch));
    }

    [Fact]
    public void The_asset_mix_is_largest_first_and_folds_small_types_into_other()
    {
        FundHolding[] holdings =
        [
            Holding("A", "Equity", 60), Holding("B", "Equity", 20), Holding("C", "Debt", 8),
            Holding("D", "Cash", 5), Holding("E", "REIT", 4), Holding("F", "Others", 3),
        ];

        (string Label, decimal Percent)[] all = [("Equity", 80m), ("Debt", 8m), ("Cash", 5m), ("Other", 7m)];
        (string Label, decimal Percent)[] few = [("Equity", 80m), ("Debt", 8m)];

        Assert.Equal(all, HoldingText.AssetMix(holdings));
        Assert.Equal(few, HoldingText.AssetMix(holdings.Take(3)));
    }

    [Fact]
    public void Holding_rows_scale_bars_to_the_largest_weight_and_mark_new_positions()
    {
        HoldingRow largest = new(Holding("HDFC Bank Ltd.", "Equity", 10) with { Symbol = "HDFCBANK", Isin = "INE040A01034", PreviousPercent = 9.5m }, 10, true);
        HoldingRow added = new(Holding("TREPS", "Cash", 0.001m), 10, true);

        Assert.Equal(HoldingRow.MaxBarWidth, largest.BarWidth);
        Assert.Equal(0.5m, largest.Change);
        Assert.False(largest.IsNew);
        Assert.Equal("HDFCBANK · INE040A01034", largest.Details);
        Assert.Equal(2, added.BarWidth);
        Assert.True(added.IsNew);
        Assert.Null(added.Change);
        Assert.Equal("Cash", added.Details);
        Assert.True(largest.Matches("hdfcbank"));
        Assert.True(added.Matches("cash"));
        Assert.False(added.Matches("bank"));
    }

    [Fact]
    public void Holdings_export_one_row_per_holding_with_the_fund_and_month()
    {
        FundHoldings fund = new(152712, "Motilal Oswal Nifty India Defence Index Fund - Direct Plan Growth", "Motilal Oswal Nifty India Defence Index Fund",
            new DateOnly(2026, 9, 1), [new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1)], Created,
            [new FundHolding("Hindustan Aeronautics Ltd.", "HAL", "INE066F01020", "Equity", "Aerospace & Defense", 19.9712m, 1_234_567m, 20.1m)]);

        string[] lines = CsvExport.Holdings(fund).Split("\r\n");

        Assert.Equal("Scheme code,Scheme name,Month,Holding,Symbol,ISIN,Asset type,Sector,Weight %,Previous month weight %,Shares", lines[0]);
        Assert.Equal(
            "152712,Motilal Oswal Nifty India Defence Index Fund - Direct Plan Growth,2026-09,Hindustan Aeronautics Ltd.,HAL,INE066F01020,"
            + "Equity,Aerospace & Defense,19.9712,20.1,1234567",
            lines[1]);
    }

    [Fact]
    public void Live_prices_show_progress_then_how_many_prices_came_back()
    {
        using CultureScope _ = new("en-IN");
        DateTime today = Created.ToLocalTime().Date;
        QuoteResultDto ok = Quote("NSE:HAL-EQ", QuoteResultStatus.Ok, 4321.5m, 1.234m);
        QuoteResultDto pending = Quote("NSE:BEL-EQ", QuoteResultStatus.Pending, null, null);
        QuoteResultDto failed = Quote("NSE:FAIL-EQ", QuoteResultStatus.Error, null, null);

        QuoteRequestDetails running = Prices(QuoteRequestStatus.Running, 0, 0, ok, pending, failed with { Status = QuoteResultStatus.Pending });
        Assert.Equal("Getting prices: 1 of 3 stocks", HoldingText.QuotesTitle(running));
        Assert.StartsWith("Requested today, ", HoldingText.QuotesDetail(running, today), StringComparison.Ordinal);
        Assert.Equal(100.0 / 3, HoldingText.QuotesPercent(running), 3);

        QuoteRequestDetails finished = Prices(QuoteRequestStatus.CompletedWithErrors, 2, 1, ok, ok with { Symbol = "NSE:BEL-EQ" }, failed);
        Assert.Equal("Live prices of 3 stocks", HoldingText.QuotesTitle(finished));
        Assert.StartsWith("2 prices · 1 failed · as of today, ", HoldingText.QuotesDetail(finished, today), StringComparison.Ordinal);
        Assert.Equal(100, HoldingText.QuotesPercent(finished));
    }

    [Theory]
    [InlineData("1.234", "+1.23%")]
    [InlineData("-0.5", "−0.50%")]
    [InlineData("0", "0.00%")]
    [InlineData(null, "—")]
    public void Day_changes_are_signed_percentages(string? percent, string expected)
    {
        using CultureScope _ = new("en-IN");

        Assert.Equal(expected, HoldingText.DayChange(percent is null ? null : decimal.Parse(percent, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Holding_rows_show_their_stocks_price_and_leave_unlisted_lines_blank()
    {
        using CultureScope _ = new("en-IN");
        FundHolding hal = new("Hindustan Aeronautics", "NSE:HAL-EQ", "INE066F01020", "Equity", null, 20m, 500m, null);
        FundHolding repo = Holding("Reverse Repo", "TREPS", 3m);

        HoldingRow priced = new(hal, 20m, false, Quote("NSE:HAL-EQ", QuoteResultStatus.Ok, 4321.5m, -0.82m));
        HoldingRow failed = new(hal, 20m, false, Quote("NSE:HAL-EQ", QuoteResultStatus.Error, null, null) with { Error = "No quote" });
        HoldingRow notRequested = new(hal, 20m, false);
        HoldingRow unlisted = new(repo, 20m, false);

        Assert.Equal(("4,321.50", "−0.82%", -0.82m), (priced.LtpText, priced.DayChangeText, priced.DayChangePercent));
        Assert.Equal(("—", "—", "NSE:HAL-EQ · No quote"), (failed.LtpText, failed.DayChangeText, failed.QuoteTip));
        Assert.Equal(("—", null), (notRequested.LtpText, notRequested.QuoteTip));
        Assert.Equal(("", ""), (unlisted.LtpText, unlisted.DayChangeText));
    }

    private static QuoteResultDto Quote(string symbol, QuoteResultStatus status, decimal? ltp, decimal? changePercent) =>
        new(symbol, status, ltp, null, changePercent, null, null, null, null, null, null, status == QuoteResultStatus.Ok ? Created : null, null);

    private static QuoteRequestDetails Prices(QuoteRequestStatus status, int succeeded, int failed, params QuoteResultDto[] results) =>
        new(Guid.NewGuid(), status, Created, Created, status.IsTerminal() ? Created : null, results.Length, succeeded, failed, null, results);

    private static HoldingFetch Fetch(HoldingFetchStatus status, params HoldingFetchFund[] funds) =>
        new(Guid.NewGuid(), status, Created, Created, status.IsTerminal() ? Created : null, funds.Length,
            funds.Count(f => f.Status.IsFinished()), null, funds);

    private static HoldingFetchFund Fund(int code, HoldingFetchFundStatus status, string? scanx = null) =>
        new(code, $"Fund {code}", status, scanx, null, null, null);

    private static FundHolding Holding(string name, string assetType, decimal percent) =>
        new(name, null, null, assetType, null, percent, null, null);
}

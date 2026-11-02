using QuoteSnap.Contracts;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Tests;

public sealed class HoldingRowTests
{
    private static readonly FundHolding Hal = new("Hindustan Aeronautics Ltd.", "NSE:HAL-EQ", "INE066F01020", "Equity", "Aerospace & Defense", 5m, null, null);
    private static readonly DateTimeOffset Fetched = new(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(5.5));

    private static QuoteResultDto Quote(decimal ltp) => new("NSE:HAL-EQ", QuoteResultStatus.Ok, ltp, 10m, 1m, null, null, null, null, null, null, Fetched, null);

    [Fact]
    public void A_newer_streamed_price_replaces_the_fetched_one_in_place()
    {
        HoldingRow row = new(Hal, 5m, false, Quote(4800m));
        List<string?> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.ApplyLive(new LiveQuote("NSE:HAL-EQ", 4850.1m, 60m, 1.25m, Fetched.AddMinutes(1)));

        Assert.Equal((4850.1m, 1.25m), (row.Ltp, row.DayChangePercent));
        Assert.Contains(nameof(HoldingRow.LtpText), changed);
        Assert.Contains(nameof(HoldingRow.DayChangeText), changed);
        Assert.StartsWith("NSE:HAL-EQ · live", row.QuoteTip);
    }

    [Fact]
    public void An_older_streamed_price_is_ignored()
    {
        HoldingRow row = new(Hal, 5m, false, Quote(4800m), new LiveQuote("NSE:HAL-EQ", 4700m, -90m, -1.88m, Fetched.AddMinutes(-5)));

        Assert.Equal((4800m, 1m), (row.Ltp, row.DayChangePercent));
    }
}

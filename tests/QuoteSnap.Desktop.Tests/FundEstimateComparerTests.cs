using QuoteSnap.Contracts;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Tests;

public sealed class FundEstimateComparerTests
{
    private static FundRow Fund(int code, decimal? change)
    {
        FundRow row = FundRow.FromMyFund(new MyFund(code, $"Fund {code}", null, null, null, DateTimeOffset.UnixEpoch, null), navsLoaded: true);
        row.ShowEstimate(change is null ? null : new NavEstimate(code, $"Fund {code}", 10m, null, null, change, null, 100m, 1, 1), null);
        return row;
    }

    private static readonly List<FundRow> Funds = [Fund(1, 0.5m), Fund(2, null), Fund(3, 2.1m), Fund(4, -1m), Fund(5, 0.5m)];

    [Theory]
    [InlineData(true, new[] { 3, 1, 5, 4, 2 })]
    [InlineData(false, new[] { 4, 1, 5, 3, 2 })]
    public void Funds_without_an_estimate_go_last_and_ties_keep_the_original_order(bool highestFirst, int[] expected)
    {
        List<FundRow> sorted = [.. Funds];
        sorted.Sort((a, b) => new FundEstimateComparer(Funds, highestFirst).Compare(a, b));

        Assert.Equal(expected, sorted.Select(f => f.SchemeCode));
    }
}
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Tests;

public sealed class ResultSetViewModelTests
{
    private static QuoteResultDto Result(string symbol, QuoteResultStatus status, string? error = null) =>
        new(symbol, status, null, null, null, null, null, null, null, null, null, null, error);

    [Fact]
    public void Counts_follow_the_results()
    {
        ResultSetViewModel results = new(new FileDialogs(), "empty")
        {
            Results =
            [
                Result("NSE:SBIN-EQ", QuoteResultStatus.Ok),
                Result("NSE:TCS-EQ", QuoteResultStatus.Ok),
                Result("NSE:BAD-EQ", QuoteResultStatus.Error, "No data"),
                Result("FOO", QuoteResultStatus.Invalid),
            ],
        };

        Assert.Equal((4, 2, 1, 1, 0), (results.Total, results.Ok, results.Errors, results.Invalid, results.Pending));
        Assert.Equal("4 rows", results.RowCountText);
    }

    [Theory]
    [InlineData("sbin", "NSE:SBIN-EQ")]
    [InlineData("no data", "NSE:BAD-EQ")]
    [InlineData("invalid", "FOO")]
    public void Search_matches_symbol_error_or_status(string search, string expected)
    {
        ResultSetViewModel results = new(new FileDialogs(), "empty")
        {
            Results =
            [
                Result("NSE:SBIN-EQ", QuoteResultStatus.Ok),
                Result("NSE:BAD-EQ", QuoteResultStatus.Error, "No data"),
                Result("FOO", QuoteResultStatus.Invalid),
            ],
        };

        results.SearchText = search;

        Assert.Equal(expected, Assert.Single(results.Visible).Symbol);
        Assert.Equal("1 of 3 rows", results.RowCountText);
    }

    [Fact]
    public void Empty_text_explains_an_empty_search()
    {
        ResultSetViewModel results = new(new FileDialogs(), "Nothing yet.") { Results = [Result("NSE:SBIN-EQ", QuoteResultStatus.Ok)] };

        results.SearchText = "zzz";

        Assert.Empty(results.Visible);
        Assert.Equal("No quotes match “zzz”.", results.EmptyText);
    }
}

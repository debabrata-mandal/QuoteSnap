using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.Tests;

public sealed class ListInputTests
{
    [Fact]
    public void Splits_on_lines_commas_semicolons_tabs_and_spaces()
    {
        List<string> entries = ListInput.Parse("NSE:SBIN-EQ\r\nNSE:TCS-EQ, NSE:INFY-EQ;NSE:ITC-EQ\tNSE:LT-EQ  NSE:HDFCBANK-EQ\n");

        Assert.Equal(["NSE:SBIN-EQ", "NSE:TCS-EQ", "NSE:INFY-EQ", "NSE:ITC-EQ", "NSE:LT-EQ", "NSE:HDFCBANK-EQ"], entries);
    }

    [Fact]
    public void Keeps_the_first_of_each_duplicate_ignoring_case_and_strips_quotes()
    {
        List<string> entries = ListInput.Parse("\"NSE:SBIN-EQ\",nse:sbin-eq,'NSE:TCS-EQ',\"\"");

        Assert.Equal(["NSE:SBIN-EQ", "NSE:TCS-EQ"], entries);
    }

    [Fact]
    public void Can_upper_case_entries()
    {
        Assert.Equal(["INF846K01WO1", "INF846K01WP8"], ListInput.Parse("inf846k01wo1\ninf846K01WP8", upperCase: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ,\r\n; ")]
    public void Blank_input_has_no_entries(string? text)
    {
        Assert.Empty(ListInput.Parse(text));
    }
}

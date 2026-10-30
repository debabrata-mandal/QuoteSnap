using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.Tests;

public sealed class CsvExportTests
{
    [Fact]
    public void Quotes_have_a_header_and_one_invariant_row_per_result()
    {
        QuoteResultDto ok = new("NSE:SBIN-EQ", QuoteResultStatus.Ok, 1234.5m, -2.25m, -0.18m, 1236m, 1240m, 1230m, 1236.75m,
            1_234_567, null, null, null);

        string[] lines = CsvExport.Quotes([ok]).Split("\r\n");

        Assert.Equal("Symbol,Status,LTP,Change,Change %,Open,High,Low,Previous close,Volume,Exchange time,Fetched at,Error", lines[0]);
        Assert.Equal("NSE:SBIN-EQ,Ok,1234.5,-2.25,-0.18,1236,1240,1230,1236.75,1234567,,,", lines[1]);
        Assert.Equal("", lines[2]);
    }

    [Fact]
    public void Text_with_separators_or_quotes_is_quoted()
    {
        string csv = CsvExport.Write(["a,b", "say \"hi\"", "two\nlines", "plain"], [("Value", s => s)]);

        Assert.Equal("Value\r\n\"a,b\"\r\n\"say \"\"hi\"\"\"\r\n\"two\nlines\"\r\nplain\r\n", csv);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Text_that_spreadsheets_would_run_as_a_formula_is_escaped(string value, string expected)
    {
        string csv = CsvExport.Write([value], [("Value", s => s)]);

        Assert.Equal($"Value\r\n{expected}\r\n", csv);
    }

    [Fact]
    public void Navs_write_dates_as_iso()
    {
        FundNav fund = new(120465, "INF846K01DP8", null, "Axis Large Cap Fund - Direct Plan - Growth", "Axis Mutual Fund",
            "Open Ended Schemes(Equity Scheme - Large Cap Fund)", "Direct Plan", "Growth Option", 67.1234m, new DateOnly(2026, 10, 29));

        string[] lines = CsvExport.Navs([fund]).Split("\r\n");

        Assert.Equal(
            "120465,INF846K01DP8,,Axis Large Cap Fund - Direct Plan - Growth,Axis Mutual Fund,"
            + "Open Ended Schemes(Equity Scheme - Large Cap Fund),Direct Plan,Growth Option,67.1234,2026-10-29",
            lines[1]);
    }
}

using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Nav;

namespace QuoteSnap.Tests;

public sealed class AmfiNavParserTests
{
    private const string File = """
        Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date
         
        Open Ended Schemes(Equity Scheme - Large Cap Fund)
         
        Axis Mutual Fund
         
        120465;INF846K01DP8;-;Axis Large Cap Fund;Direct Plan;Growth Option;67.1234;29-Oct-2026
        120466;INF846K01DQ6;INF846K01DR4;Axis Large Cap Fund;Direct Plan;IDCW Option;25.5;29-Oct-2026
         
        HDFC Mutual Fund
         
        100001;Redeemed;HDFCNIVODG;HDFC Old Fund;Regular Plan;Growth;2540201.95080000;02-Jul-2018
        100002;INF179K01XX1;-;HDFC Broken NAV;Regular Plan;Growth;N.A.;29-Oct-2026
        100003;INF179K01XX2;-;HDFC Broken Date;Regular Plan;Growth;10.5;2026-10-29
         
        Close Ended Schemes(Income)
         
        IL&FS Mutual Fund (IDF)
         
        100004;INF613Q01019;-;IL&FS Infra Debt Fund;Regular Plan;Growth;15.0000;29-Oct-2026
        """;

    [Fact]
    public void Reads_rows_with_their_category_and_fund_house()
    {
        AmfiNavFile file = AmfiNavParser.Parse(File.ReplaceLineEndings("\r\n"));

        Assert.Equal([120465, 120466, 100001, 100004], file.Navs.Select(n => n.SchemeCode));
        Assert.Equal(2, file.SkippedRows);

        MutualFundNav growth = file.Navs[0];
        Assert.Equal("INF846K01DP8", growth.Isin);
        Assert.Null(growth.IsinReinvestment);
        Assert.Equal("Axis Large Cap Fund", growth.SchemeName);
        Assert.Equal("Axis Mutual Fund", growth.FundHouse);
        Assert.Equal("Open Ended Schemes(Equity Scheme - Large Cap Fund)", growth.Category);
        Assert.Equal("Direct Plan", growth.Plan);
        Assert.Equal("Growth Option", growth.Option);
        Assert.Equal(67.1234m, growth.Nav);
        Assert.Equal(new DateOnly(2026, 10, 29), growth.NavDate);

        Assert.Equal("INF846K01DR4", file.Navs[1].IsinReinvestment);

        MutualFundNav old = file.Navs[2];
        Assert.Null(old.Isin);
        Assert.Null(old.IsinReinvestment);
        Assert.Equal(2540201.9508m, old.Nav);
        Assert.Equal("HDFC Mutual Fund", old.FundHouse);

        MutualFundNav ilfs = file.Navs[3];
        Assert.Equal("IL&FS Mutual Fund (IDF)", ilfs.FundHouse);
        Assert.Equal("Close Ended Schemes(Income)", ilfs.Category);
    }

    [Fact]
    public void Reads_the_older_layout_without_plan_and_option()
    {
        AmfiNavFile file = AmfiNavParser.Parse("""
            Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Net Asset Value;Date
            Axis Mutual Fund
            120465;INF846K01DP8;-;Axis Large Cap Fund - Direct Plan - Growth;67.1234;29-Oct-2026
            """);

        MutualFundNav nav = Assert.Single(file.Navs);
        Assert.Equal("Axis Large Cap Fund - Direct Plan - Growth", nav.SchemeName);
        Assert.Null(nav.Plan);
        Assert.Null(nav.Option);
        Assert.Equal(67.1234m, nav.Nav);
    }

    [Theory]
    [InlineData("<html>Service unavailable</html>")]
    [InlineData("Scheme Code;Scheme Name;Date")]
    public void Rejects_a_file_without_the_expected_header(string text)
    {
        Assert.Throws<FormatException>(() => AmfiNavParser.Parse(text));
    }
}

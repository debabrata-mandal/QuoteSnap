using QuoteSnap.Api.Nav;

namespace QuoteSnap.Tests;

public sealed class AmfiSchemeMasterTests
{
    [Fact]
    public void Reads_nav_names_by_scheme_code()
    {
        IReadOnlyDictionary<int, string> navNames = AmfiSchemeMaster.ParseNavNames("""
            AMC,Code,Scheme Name,Scheme Type,Scheme Category,Scheme NAV Name,Scheme Minimum Amount,Launch Date, Closure Date,ISIN Div Payout/ ISIN GrowthISIN Div Reinvestment
            Motilal Oswal Asset Management Company Limited,127042,Motilal Oswal Midcap Fund,Open Ended,Equity Scheme - Mid Cap Fund,Motilal Oswal Midcap Fund-Direct Plan-Growth Option,Rs. 500/-,03-Feb-2014,,INF247L01445
            Some AMC,1,Fund, with a comma,Open Ended,Equity,Fund - Direct Plan - Growth,500,01-Jan-2020,,
            HSBC Asset Management (India) Private Ltd.,120046,HSBC Flexi Cap Fund,Open Ended,Equity Schemes - Flexi Cap Fund,HSBC Flexi Cap Fund - Direct Growth,5000,22-Jan-2004,,INF336L01DH5

            """.ReplaceLineEndings("\r\n"));

        Assert.Equal(2, navNames.Count);
        Assert.Equal("Motilal Oswal Midcap Fund-Direct Plan-Growth Option", navNames[127042]);
        Assert.Equal("HSBC Flexi Cap Fund - Direct Growth", navNames[120046]);
    }

    [Fact]
    public void A_file_without_nav_names_is_rejected()
    {
        Assert.Throws<FormatException>(() => AmfiSchemeMaster.ParseNavNames("<html>maintenance</html>"));
    }

    [Theory]
    [InlineData("Motilal Oswal Midcap Fund-Direct Plan-Growth Option", true)]
    [InlineData("Motilal Oswal Consumption Fund-Direct-IDCW", true)]
    [InlineData("HSBC Ultra Short Term Fund - Direct Growth", true)]
    [InlineData("HDFC FMP 1204D December - Quarterly IDCW Option - Direct Plan", true)]
    [InlineData("Motilal Oswal Midcap Fund-Regular Plan-Growth Option", false)]
    [InlineData("BARODA BNP PARIBAS Corporate Bond Fund - Defunct Plan - Annual-IDCW Option", false)]
    [InlineData("Kotak Silver ETF", false)]
    public void Direct_plans_are_recognised_by_name(string navName, bool expected)
    {
        Assert.Equal(expected, AmfiSchemeMaster.IsDirect(navName));
    }

    [Theory]
    [InlineData("Motilal Oswal Midcap Fund-Direct Plan-Growth Option", "Motilal Oswal Midcap Fund", "Growth Option")]
    [InlineData("HSBC Short Term Fund - Direct Plan -  Monthly IDCW", "HSBC Short Term Fund", "Monthly IDCW")]
    [InlineData("HSBC Ultra Short Term Fund - Direct Growth", "HSBC Ultra Short Term Fund", "Growth")]
    [InlineData("Motilal Oswal Liquid Fund Direct - IDCW Fortnightly Reinvestment", "Motilal Oswal Liquid Fund", "IDCW Fortnightly Reinvestment")]
    [InlineData("HDFC FMP 1204D December - Quarterly IDCW Option - Direct Plan", "HDFC FMP 1204D December", "Quarterly IDCW Option")]
    [InlineData("PGIM India Arbitrage Fund - Direct Plan - Half-Yearly Dividend", "PGIM INDIA ARBITRAGE FUND", "Half-Yearly Dividend")]
    [InlineData("Motilal Oswal BSE Low Volatility Index Fund-Direct plan", "Motilal Oswal BSE Low Volatility Index Fund", null)]
    [InlineData("Some Fund - Quarterly IDCW - Direct Plan", "A differently named fund", null)]
    public void The_option_is_whatever_the_name_adds_besides_the_plan(string navName, string schemeName, string? expected)
    {
        Assert.Equal(expected, AmfiSchemeMaster.OptionFrom(navName, schemeName));
    }
}

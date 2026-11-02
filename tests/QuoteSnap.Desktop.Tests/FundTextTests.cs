using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.Tests;

public sealed class FundTextTests
{
    [Theory]
    [InlineData("Growth", "Growth")]
    [InlineData("GROWTH", "Growth")]
    [InlineData("Growth Option", "Growth")]
    [InlineData("Direct Growth", "Growth")]
    [InlineData("IDCW", "IDCW")]
    [InlineData("IDCW Option", "IDCW")]
    [InlineData("IDCW (Income Distribution CUM Capital Withdrawal)", "IDCW")]
    [InlineData("Income Distribution cum capital withdrawal", "IDCW")]
    [InlineData("Payout/Reinvestment", "IDCW")]
    [InlineData("Monthly IDCW", "Monthly IDCW")]
    [InlineData("QUARTERLY IDCW Option", "Quarterly IDCW")]
    [InlineData("Quaterly IDCW", "Quarterly IDCW")]
    [InlineData("Bonus Option", "Bonus")]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    public void Options_get_one_consistent_label(string? option, string? expected)
    {
        Assert.Equal(expected, FundText.Option(option));
    }

    [Theory]
    [InlineData("Open Ended Schemes(Equity Scheme - Large & Mid Cap Fund)", new[] { "Equity", "Large & Mid Cap" })]
    [InlineData("Open Ended Schemes(Equity Schemes - Large & Mid Cap Fund)", new[] { "Equity", "Large & Mid Cap" })]
    [InlineData("Open Ended Schemes(Other Scheme - Index Funds)", new[] { "Other", "Index Funds" })]
    [InlineData("Open Ended Schemes(Equity Scheme - Sectoral/ Thematic)", new[] { "Equity", "Sectoral/Thematic" })]
    [InlineData("Open Ended Schemes(Income/Debt Oriented Schemes - Liquid Fund)", new[] { "Income/Debt", "Liquid" })]
    [InlineData("Open Ended Schemes(Fund of Funds Scheme (Domestic) - Fund of Funds Scheme (Domestic))", new[] { "Fund of Funds (Domestic)" })]
    [InlineData("Close Ended Schemes(Income)", new[] { "Income" })]
    public void Categories_become_an_asset_class_and_a_sub_category(string category, string[] expected)
    {
        Assert.Equal(expected, FundText.Category(category));
    }

    [Fact]
    public void The_subtitle_reads_like_a_brokerage_app()
    {
        Assert.Equal("Growth · Equity · Large & Mid Cap",
            FundText.Subtitle("Growth Option", "Open Ended Schemes(Equity Scheme - Large & Mid Cap Fund)"));
        Assert.Equal("Equity", FundText.Subtitle(null, "Open Ended Schemes(Equity Scheme)"));
    }

    [Theory]
    [InlineData("Nippon India Mutual Fund", "NI")]
    [InlineData("Motilal Oswal Mutual Fund", "MO")]
    [InlineData("DSP Mutual Fund", "DSP")]
    [InlineData("HDFC Mutual Fund", "HDFC")]
    [InlineData("PPFAS Mutual Fund", "PP")]
    [InlineData("Zerodha Mutual Fund", "Z")]
    [InlineData(null, "AL")]
    public void Initials_come_from_the_fund_house(string? fundHouse, string expected)
    {
        Assert.Equal(expected, FundText.Initials(fundHouse, "Axis Large Cap Fund"));
    }

    [Fact]
    public void Avatar_colours_are_stable_per_fund_house()
    {
        Assert.Equal(FundText.AvatarColor("DSP Mutual Fund"), FundText.AvatarColor("dsp mutual fund"));
        Assert.StartsWith("#", FundText.AvatarColor(null));
    }

    [Theory]
    [InlineData("1161.81", "1,161.81")]
    [InlineData("402.6856", "402.6856")]
    [InlineData("25.5", "25.50")]
    public void Navs_show_between_two_and_four_decimals(string nav, string expected)
    {
        using CultureScope _ = new("en-IN");

        Assert.Equal(expected, FundText.Nav(decimal.Parse(nav, System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static NavEstimate Estimate(DateOnly? month = null, decimal? change = 0.2204m) =>
        new(1, "Defence Fund", 14.285m, new DateOnly(2026, 10, 29), month, change, change is null ? null : 14.3165m, 17.5m, 2, 3);

    [Fact]
    public void The_estimate_tip_says_what_the_estimate_is_based_on()
    {
        using CultureScope _ = new("en-IN");
        DateTimeOffset pricesAt = DateTimeOffset.Now;

        string tip = FundText.EstimateTip(Estimate(new DateOnly(2026, 9, 1)), pricesAt, pricesAt.LocalDateTime.Date);

        Assert.StartsWith($"From 2 of 3 holdings (17.50% of assets) in the {HoldingText.Month(new DateOnly(2026, 9, 1))} portfolio; the rest count as unchanged · on the NAV of 29 Oct 2026 · prices today", tip);
    }

    [Fact]
    public void The_estimate_tip_explains_a_missing_estimate()
    {
        Assert.Equal("No holdings yet. Fetch them on the Holdings page to estimate this fund.",
            FundText.EstimateTip(Estimate(), DateTimeOffset.Now));
        Assert.Equal("No live prices yet. Press Get live prices to estimate today's NAV.",
            FundText.EstimateTip(Estimate(new DateOnly(2026, 9, 1), null), null));
        Assert.Equal("None of its 3 holdings has a live price, so it can't be estimated.",
            FundText.EstimateTip(Estimate(new DateOnly(2026, 9, 1), null), DateTimeOffset.Now));
    }
}

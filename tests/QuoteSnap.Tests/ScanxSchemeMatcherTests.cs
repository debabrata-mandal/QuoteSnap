using QuoteSnap.Api.Holdings;

namespace QuoteSnap.Tests;

public sealed class ScanxSchemeMatcherTests
{
    private static readonly IReadOnlyList<ScanxScheme> Schemes =
    [
        new("DSP Mutual Fund", 16438, "DSP Midcap Direct Plan-Growth"),
        new("DSP Mutual Fund", 16425, "DSP Large & Mid Cap Fund Direct Plan-Growth"),
        new("DSP Mutual Fund", 16456, "DSP Large Cap Fund Direct-Growth"),
        new("DSP Mutual Fund", 43892, "DSP Banking & Financial Services Fund Direct-Growth"),
        new("DSP Mutual Fund", 16410, "DSP India T.I.G.E.R. (The Infrastructure Growth and Economic Reforms Fund) Direct-Growth"),
        new("Motilal Oswal Mutual Fund", 44397, "Motilal Oswal Nifty India Defence Index Fund Direct-Growth"),
        new("Motilal Oswal Mutual Fund", 40242, "Motilal Oswal Nifty Midcap 150 Index Fund Direct-Growth"),
        new("Motilal Oswal Mutual Fund", 24080, "Motilal Oswal Midcap Fund Direct-Growth"),
        new("Axis Mutual Fund", 45950, "Axis Nifty India Defence Index Fund Direct-Growth"),
        new("Axis Mutual Fund", 50001, "Axis Global Innovation FoF Direct-Growth"),
        new("Axis Mutual Fund", 50002, "Axis CRISIL IBX 50:50 Gilt Plus SDL Jun 2028 Index Fund Direct-Growth"),
        new("Sundaram Mutual Fund", 35008, "Sundaram Long Term Tax Advantage Fund Series III Direct-Growth"),
        new("ICICI Prudential Mutual Fund", 42229, "ICICI Prudential Nifty SDL Sep 2027 Index Fund Direct-Growth"),
        new("PPFAS Mutual Fund", 19701, "Parag Parikh Flexi Cap Fund Direct-Growth"),
    ];

    [Theory]
    [InlineData("DSP Midcap Fund", "DSP Mutual Fund", 16438)]
    [InlineData("DSP Large & Mid Cap Fund", "DSP Mutual Fund", 16425)]
    [InlineData("DSP Large Cap Fund", "DSP Mutual Fund", 16456)]
    [InlineData("DSP Banking and Financial Services Fund", "DSP Mutual Fund", 43892)]
    [InlineData("DSP India T.I.G.E.R. Fund (The Infrastructure Growth and Economic Reforms Fund)", "DSP Mutual Fund", 16410)]
    [InlineData("Motilal Oswal Nifty India Defence Index Fund", "Motilal Oswal Mutual Fund", 44397)]
    [InlineData("Motilal Oswal Nifty Midcap150 Index Fund", "Motilal Oswal Mutual Fund", 40242)]
    [InlineData("Motilal Oswal Midcap Fund - Direct Plan - Growth Option", "Motilal Oswal Mutual Fund", 24080)]
    [InlineData("Axis Global Innovation Fund of Fund", "Axis Mutual Fund", 50001)]
    [InlineData("Axis CRISIL IBX 50:50 Gilt Plus SDL June 2028 Index Fund", "Axis Mutual Fund", 50002)]
    [InlineData("Parag Parikh Flexi Cap Fund", "PPFAS Mutual Fund", 19701)]
    [InlineData("Parag Parikh Flexi Cap Fund", null, 19701)]
    public void Finds_the_scanx_scheme(string schemeName, string? fundHouse, int planId)
    {
        Assert.Equal(planId, ScanxSchemeMatcher.Match(schemeName, fundHouse, Schemes)?.PlanId);
    }

    [Fact]
    public void The_fund_house_picks_between_schemes_with_the_same_name()
    {
        Assert.Equal(45950, ScanxSchemeMatcher.Match("Axis Nifty India Defence Index Fund", "Axis Mutual Fund", Schemes)?.PlanId);
    }

    [Theory]
    [InlineData("DSP Small Cap Fund", "DSP Mutual Fund")]
    [InlineData("Sundaram Long Term Tax Advantage Fund Series IV", "Sundaram Mutual Fund")]
    [InlineData("ICICI Prudential Nifty SDL Sep 2026 Index Fund", "ICICI Prudential Mutual Fund")]
    [InlineData("HDFC Liquid Fund", "HDFC Mutual Fund")]
    [InlineData("Nifty India Defence Index Fund", "Unknown Mutual Fund")]
    public void Different_funds_are_not_matched(string schemeName, string? fundHouse)
    {
        Assert.Null(ScanxSchemeMatcher.Match(schemeName, fundHouse, Schemes));
    }

    [Theory]
    [InlineData("DSP Large & Mid Cap Fund - Direct Plan - Growth", "dsplargemidcap")]
    [InlineData("Motilal Oswal Nifty Midcap 150 Index Fund", "motilaloswalniftymidcap150index")]
    [InlineData("Axis Global Innovation Fund of Funds", "axisglobalinnovationfof")]
    [InlineData("Franklin India Opportunities Fund (formerly Franklin India Opportunities Fund)", "franklinindiaopportunities")]
    public void Keys_drop_plans_options_and_filler(string name, string key)
    {
        Assert.Equal(key, ScanxSchemeMatcher.Key(name));
    }
}

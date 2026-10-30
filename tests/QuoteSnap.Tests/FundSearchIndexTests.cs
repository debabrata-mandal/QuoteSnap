using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Nav;

namespace QuoteSnap.Tests;

public sealed class FundSearchIndexTests
{
    private static readonly FundSearchIndex Index = FundSearchIndex.Build(
    [
        Fund(120596, "ICICI Prudential Large & Mid Cap Fund", "ICICI Prudential Mutual Fund", "Growth", "INF109K01Z48"),
        Fund(120597, "ICICI Prudential Large & Mid Cap Fund", "ICICI Prudential Mutual Fund", "IDCW"),
        Fund(152482, "ICICI Prudential Nifty LargeMidcap 250 Index Fund", "ICICI Prudential Mutual Fund", "Growth"),
        Fund(147622, "Motilal Oswal Nifty 500 Index Fund", "Motilal Oswal Mutual Fund", "Growth"),
        Fund(147794, "Motilal Oswal Nifty Next 50 Index Fund", "Motilal Oswal Mutual Fund", "Growth"),
        Fund(129046, "Motilal Oswal Flexi Cap Fund", "Motilal Oswal Mutual Fund", "Growth"),
        Fund(119076, "DSP Flexi Cap Fund", "DSP Mutual Fund", "Growth"),
        Fund(118763, "Nippon India Power & Infra Fund", "Nippon India Mutual Fund", "Growth Option"),
        Fund(118764, "Nippon India Power & Infra Fund", "Nippon India Mutual Fund", "IDCW Option"),
        Fund(118778, "Nippon India Growth Mid Cap Fund", "Nippon India Mutual Fund", "Growth Plan"),
        Fund(149870, "Zerodha Nifty LargeMidcap 250 Index Fund", "Zerodha Mutual Fund", "Growth"),
        Fund(122639, "Parag Parikh Flexi Cap Fund", "PPFAS Mutual Fund", "Growth"),
        Fund(119436, "Aditya Birla Sun Life Large & Mid Cap Fund", "Aditya Birla Sun Life Mutual Fund", "GROWTH"),
        Fund(119300, "BANK OF INDIA LARGE & MID CAP FUND", "Bank of India Mutual Fund", "Growth"),
        Fund(153797, "Groww Nifty Next 50 Index Fund", "Groww Mutual Fund", "Income Distribution cum capital withdrawal"),
    ]);

    [Theory]
    [InlineData("nippon power infra", 118763)]
    [InlineData("Nippon India Power & Infra Fund", 118763)]
    [InlineData("power and infra nippon", 118763)]
    [InlineData("NIPPON POWER", 118763)]
    [InlineData("icici pru large mid", 120596)]
    [InlineData("icici prudential large and midcap", 120596)]
    [InlineData("motilal next 50", 147794)]
    [InlineData("motilal nifty next50", 147794)]
    [InlineData("motilal flexi", 129046)]
    [InlineData("zerodha large midcap 250", 149870)]
    [InlineData("zerodha largemidcap250", 149870)]
    [InlineData("dsp flexicap", 119076)]
    [InlineData("ppfas flexi", 122639)]
    [InlineData("parag parikh", 122639)]
    [InlineData("absl large mid", 119436)]
    [InlineData("bank of india large mid", 119300)]
    [InlineData("nippon growth mid cap", 118778)]
    public void Finds_the_scheme_however_its_name_is_typed(string query, int expected)
    {
        FundSearchResult result = Index.Search(query, 10);

        Assert.False(result.Partial);
        Assert.Equal(expected, result.Funds[0].SchemeCode);
    }

    [Theory]
    [InlineData("motial oswal flexi", 129046)]
    [InlineData("icici prudentail large mid", 120596)]
    [InlineData("nipon power infra", 118763)]
    [InlineData("parag parik", 122639)]
    public void Tolerates_small_typos(string query, int expected)
    {
        Assert.Equal(expected, Index.Search(query, 10).Funds[0].SchemeCode);
    }

    [Fact]
    public void Growth_comes_before_idcw_for_the_same_scheme()
    {
        IReadOnlyList<MutualFundNav> funds = Index.Search("nippon power infra", 10).Funds;

        Assert.Equal([118763, 118764], funds.Select(f => f.SchemeCode));
    }

    [Fact]
    public void Idcw_and_dividend_find_the_payout_options()
    {
        Assert.Equal(118764, Index.Search("nippon power idcw", 10).Funds[0].SchemeCode);
        Assert.Equal(118764, Index.Search("nippon power dividend", 10).Funds[0].SchemeCode);
        Assert.Equal(153797, Index.Search("groww next 50 idcw", 10).Funds[0].SchemeCode);
    }

    [Fact]
    public void Every_word_must_match_unless_nothing_does()
    {
        FundSearchResult flexi = Index.Search("flexi cap", 10);
        Assert.False(flexi.Partial);
        Assert.Equal([119076, 122639, 129046], flexi.Funds.Select(f => f.SchemeCode).Order());

        FundSearchResult closest = Index.Search("dsp flexi smallcase", 10);
        Assert.True(closest.Partial);
        Assert.Equal(119076, closest.Funds[0].SchemeCode);

        FundSearchResult renamed = Index.Search("nippon emerging bluechip", 10);
        Assert.True(renamed.Partial);
        Assert.NotEmpty(renamed.Funds);
        Assert.All(renamed.Funds, f => Assert.StartsWith("Nippon", f.SchemeName, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("INF109K01Z48", 120596)]
    [InlineData("inf109k01z48", 120596)]
    [InlineData("118778", 118778)]
    public void Isins_and_scheme_codes_work_too(string query, int expected)
    {
        Assert.Equal(expected, Assert.Single(Index.Search(query, 10).Funds).SchemeCode);
    }

    [Fact]
    public void An_isin_one_character_off_finds_nothing()
    {
        Assert.Empty(Index.Search("INF109K01Z49", 10).Funds);
    }

    [Theory]
    [InlineData("fund")]
    [InlineData("direct plan")]
    [InlineData("hdfc")]
    [InlineData("xyzzy")]
    public void Unknown_or_meaningless_searches_find_nothing(string query)
    {
        Assert.Empty(Index.Search(query, 10).Funds);
    }

    [Fact]
    public void Results_are_limited()
    {
        Assert.Equal(3, Index.Search("nifty", 3).Funds.Count);
    }

    [Fact]
    public void Schemes_with_a_current_nav_come_before_matured_ones()
    {
        MutualFundNav matured = Fund(148238, "UTI Credit Risk Fund", "UTI Mutual Fund", "Growth");
        matured.NavDate = new DateOnly(2020, 3, 6);
        FundSearchIndex index = FundSearchIndex.Build(
        [
            matured,
            Fund(128463, "UTI Credit Risk Fund (Segregated Portfolio Wound Up)", "UTI Mutual Fund", "Growth"),
        ]);

        Assert.Equal([128463, 148238], index.Search("uti credit risk", 10).Funds.Select(f => f.SchemeCode));
    }

    private static MutualFundNav Fund(int code, string name, string house, string option, string? isin = null) => new()
    {
        SchemeCode = code,
        SchemeName = name,
        FundHouse = house,
        Option = option,
        Plan = "Direct Plan",
        Isin = isin,
        Nav = 100m,
        NavDate = new DateOnly(2026, 10, 29),
    };
}

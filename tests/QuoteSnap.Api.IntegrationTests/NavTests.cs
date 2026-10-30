using System.Net;
using System.Net.Http.Json;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class NavTests
{
    private const string Header =
        "Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date";

    private const string File = $"""
        {Header}

        Open Ended Schemes(Equity Scheme - Large Cap Fund)

        Axis Mutual Fund

        120465;INF846K01DP8;-;Axis Large Cap Fund;Direct Plan;Growth Option;67.1234;29-Oct-2026
        120466;INF846K01DQ6;INF846K01DR4;Axis Large Cap Fund;Direct Plan;IDCW Option;25.5;29-Oct-2026
        120467;INF846K01DS2;-;Axis Large Cap Fund;Regular Plan;Growth Option;60.25;29-Oct-2026
        """;

    [Fact]
    public async Task Lookup_returns_navs_by_either_isin_in_request_order()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        NavRefreshResult refresh = await client.RefreshNavsAsync();
        Assert.Equal(2, refresh.SchemeCount);
        Assert.Equal(new DateOnly(2026, 10, 29), refresh.LatestNavDate);

        NavLookup lookup = await client.GetNavsAsync(["INF846K01DR4", "inf846k01dp8", "INF000000000", "INF846K01DQ6"]);

        Assert.Equal([120466, 120465], lookup.Funds.Select(f => f.SchemeCode));
        Assert.Equal(["INF000000000"], lookup.NotFound);
        Assert.Equal(refresh.LoadedAt, lookup.LoadedAt);

        FundNav growth = lookup.Funds[1];
        Assert.Equal("Axis Large Cap Fund", growth.SchemeName);
        Assert.Equal("Axis Mutual Fund", growth.FundHouse);
        Assert.Equal("Direct Plan", growth.Plan);
        Assert.Equal("Growth Option", growth.Option);
        Assert.Equal(67.1234m, growth.Nav);
        Assert.Equal(new DateOnly(2026, 10, 29), growth.NavDate);
    }

    [Fact]
    public async Task Refresh_replaces_the_stored_navs()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        factory.NavFile = $"""
            {Header}
            Axis Mutual Fund
            120465;INF846K01DP8;-;Axis Large Cap Fund;Direct Plan;Growth Option;68.0;30-Oct-2026
            """;
        await client.RefreshNavsAsync();

        NavLookup lookup = await client.GetNavsAsync(["INF846K01DP8", "INF846K01DQ6"]);
        FundNav fund = Assert.Single(lookup.Funds);
        Assert.Equal(68.0m, fund.Nav);
        Assert.Equal(new DateOnly(2026, 10, 30), fund.NavDate);
        Assert.Equal(["INF846K01DQ6"], lookup.NotFound);
    }

    [Fact]
    public async Task Only_direct_plans_are_stored()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        NavLookup lookup = await client.GetNavsAsync(["INF846K01DP8", "INF846K01DS2"]);

        Assert.Equal("Direct Plan", Assert.Single(lookup.Funds).Plan);
        Assert.Equal(["INF846K01DS2"], lookup.NotFound);
    }

    // AMFI lists some schemes without Plan and Option; only the scheme master's NAV name tells them apart.
    private const string FileWithoutPlans = $"""
        {Header}
        Open Ended Schemes(Equity Scheme - Mid Cap Fund)
        Motilal Oswal Mutual Fund
        127039;INF247L01411;-;Motilal Oswal Midcap Fund;;;97.0037;29-Oct-2026
        127042;INF247L01445;-;Motilal Oswal Midcap Fund;;;112.2349;29-Oct-2026
        120465;INF846K01DP8;-;Axis Large Cap Fund;Direct Plan;Growth Option;67.1234;29-Oct-2026
        """;

    private const string SchemeMaster = """
        AMC,Code,Scheme Name,Scheme Type,Scheme Category,Scheme NAV Name,Scheme Minimum Amount,Launch Date, Closure Date,ISIN Div Payout/ ISIN GrowthISIN Div Reinvestment
        Motilal Oswal Asset Management Company Limited,127039,Motilal Oswal Midcap Fund,Open Ended,Equity Scheme - Mid Cap Fund,Motilal Oswal Midcap Fund-Regular Plan-Growth Option,Rs. 500/-,03-Feb-2014,,INF247L01411
        Motilal Oswal Asset Management Company Limited,127042,Motilal Oswal Midcap Fund,Open Ended,Equity Scheme - Mid Cap Fund,Motilal Oswal Midcap Fund-Direct Plan-Growth Option,Rs. 500/-,03-Feb-2014,,INF247L01445
        """;

    [Fact]
    public async Task Schemes_without_a_plan_are_kept_when_the_scheme_master_says_direct()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = FileWithoutPlans, SchemeMasterFile = SchemeMaster };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        Assert.Equal(2, (await client.RefreshNavsAsync()).SchemeCount);

        NavLookup lookup = await client.GetNavsBySchemeCodeAsync([127042, 127039]);
        FundNav midcap = Assert.Single(lookup.Funds);
        Assert.Equal(127042, midcap.SchemeCode);
        Assert.Equal("Direct Plan", midcap.Plan);
        Assert.Equal("Growth Option", midcap.Option);
        Assert.Equal(["127039"], lookup.NotFound);
        Assert.Equal(127042, (await client.SearchNavsAsync("motilal midcap")).Funds[0].SchemeCode);
    }

    [Fact]
    public async Task Navs_still_refresh_when_the_scheme_master_is_unavailable()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = FileWithoutPlans };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        Assert.Equal(1, (await client.RefreshNavsAsync()).SchemeCount);
        Assert.Equal(["127042"], (await client.GetNavsBySchemeCodeAsync([127042])).NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<html>maintenance</html>")]
    public async Task A_failed_refresh_returns_502_and_keeps_the_stored_navs(string? badFile)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        factory.NavFile = badFile;
        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.RefreshNavsAsync());

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Single((await client.GetNavsAsync(["INF846K01DP8"])).Funds);
    }

    [Theory]
    [InlineData("?isin=INF846K01DP8")]
    [InlineData("?isin=INF846K01DP8,INF846K01DQ6")]
    public async Task Lookup_accepts_one_isin_or_a_comma_separated_list(string query)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        await factory.CreateQuoteSnapClient().RefreshNavsAsync();
        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(QuoteSnapHeaders.ApiKey, QuoteSnapApiFactory.ApiKey);

        NavLookup? lookup = await http.GetFromJsonAsync<NavLookup>("/api/nav" + query);

        Assert.NotNull(lookup);
        Assert.Contains(lookup.Funds, f => f.SchemeCode == 120465);
        Assert.Empty(lookup.NotFound);
    }

    [Fact]
    public async Task Lookup_before_the_first_download_returns_503()
    {
        await using QuoteSnapApiFactory factory = new();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() =>
            factory.CreateQuoteSnapClient().GetNavsAsync(["INF846K01DP8"]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task Status_reports_what_the_last_refresh_loaded()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        Assert.Equal(new NavStatus(0, null, null), await client.GetNavStatusAsync());

        NavRefreshResult refresh = await client.RefreshNavsAsync();

        Assert.Equal(new NavStatus(2, new DateOnly(2026, 10, 29), refresh.LoadedAt), await client.GetNavStatusAsync());
    }

    [Fact]
    public async Task Search_finds_schemes_by_a_loosely_typed_name()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        NavRefreshResult refresh = await client.RefreshNavsAsync();

        NavSearchResult result = await client.SearchNavsAsync("axis largecap");

        Assert.False(result.Partial);
        Assert.Equal([120465, 120466], result.Funds.Select(f => f.SchemeCode));
        Assert.Equal(refresh.LoadedAt, result.LoadedAt);
    }

    [Fact]
    public async Task Search_sees_the_navs_of_the_latest_refresh()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        Assert.Empty((await client.SearchNavsAsync("hdfc flexi")).Funds);

        factory.NavFile = $"""
            {Header}
            HDFC Mutual Fund
            118955;INF179K01UT0;-;HDFC Flexi Cap Fund;Direct Plan;Growth Option;2100.5;30-Oct-2026
            """;
        await client.RefreshNavsAsync();

        FundNav fund = Assert.Single((await client.SearchNavsAsync("hdfc flexi")).Funds);
        Assert.Equal(2100.5m, fund.Nav);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public async Task Search_needs_at_least_two_characters(string query)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SearchNavsAsync(query));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Navs_by_scheme_code_keep_the_requested_order()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        NavLookup lookup = await client.GetNavsBySchemeCodeAsync([120466, 999999, 120465, 120466]);

        Assert.Equal([120466, 120465], lookup.Funds.Select(f => f.SchemeCode));
        Assert.Equal(["999999"], lookup.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12x")]
    public async Task Navs_by_scheme_code_reject_missing_or_invalid_codes(string codes)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(QuoteSnapHeaders.ApiKey, QuoteSnapApiFactory.ApiKey);

        using HttpResponseMessage response = await http.GetAsync("/api/nav/schemes?code=" + codes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_and_scheme_lookup_before_the_first_download_return_503()
    {
        await using QuoteSnapApiFactory factory = new();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        QuoteSnapApiException search = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SearchNavsAsync("axis"));
        QuoteSnapApiException schemes = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.GetNavsBySchemeCodeAsync([120465]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, schemes.StatusCode);
    }

    [Fact]
    public async Task Lookup_without_an_isin_returns_400()
    {
        await using QuoteSnapApiFactory factory = new();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() =>
            factory.CreateQuoteSnapClient().GetNavsAsync([]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Refresh_requires_the_api_key()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        using HttpClient http = factory.CreateClient();

        using HttpResponseMessage response = await http.PostAsync("/api/nav/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

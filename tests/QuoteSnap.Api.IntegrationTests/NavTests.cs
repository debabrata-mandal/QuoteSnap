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

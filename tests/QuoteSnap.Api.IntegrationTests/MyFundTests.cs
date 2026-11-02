using System.Net;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class MyFundTests
{
    private const string Header =
        "Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date";

    private const string File = $"""
        {Header}
        Open Ended Schemes(Equity Scheme - Sectoral/ Thematic)
        DSP Mutual Fund
        152045;INF740KA1TB3;-;DSP Banking & Financial Services Fund;Direct Plan;Growth;14.285;29-Oct-2026
        Open Ended Schemes(Other Scheme - Index Funds)
        Motilal Oswal Mutual Fund
        152712;INF247L01DH2;-;Motilal Oswal Nifty India Defence Index Fund;Direct Plan;Growth;11.6296;29-Oct-2026
        152713;INF247L01DI0;-;Motilal Oswal Nifty India Defence Index Fund;Regular Plan;Growth;11.5;29-Oct-2026
        """;

    [Fact]
    public async Task Funds_are_added_listed_with_their_navs_and_removed()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        Assert.Empty((await client.GetMyFundsAsync()).Funds);

        MyFund added = await client.AddMyFundAsync(152712);
        Assert.Equal("Motilal Oswal Nifty India Defence Index Fund", added.SchemeName);
        Assert.Equal("Motilal Oswal Mutual Fund", added.FundHouse);
        Assert.Equal("Growth", added.Option);
        Assert.Equal(11.6296m, added.Latest?.Nav);
        await client.AddMyFundAsync(152045);

        MyFundList list = await client.GetMyFundsAsync();
        Assert.Equal([152712, 152045], list.Funds.Select(f => f.SchemeCode));
        Assert.Equal(14.285m, list.Funds[1].Latest?.Nav);
        Assert.Equal(added.AddedAt, list.Funds[0].AddedAt);
        Assert.NotNull(list.LoadedAt);

        await client.RemoveMyFundAsync(152712);
        await client.RemoveMyFundAsync(152712);
        Assert.Equal([152045], (await client.GetMyFundsAsync()).Funds.Select(f => f.SchemeCode));
    }

    [Fact]
    public async Task The_list_keeps_the_users_order_and_new_funds_go_last()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(152712);
        await client.AddMyFundAsync(152045);

        await client.ReorderMyFundsAsync([152045, 152712]);
        Assert.Equal([152045, 152712], (await client.GetMyFundsAsync()).Funds.Select(f => f.SchemeCode));

        await client.RemoveMyFundAsync(152045);
        await client.AddMyFundAsync(152045);
        Assert.Equal([152712, 152045], (await client.GetMyFundsAsync()).Funds.Select(f => f.SchemeCode));
    }

    [Theory]
    [InlineData(new[] { 152045 })]
    [InlineData(new[] { 152045, 152045 })]
    [InlineData(new[] { 152045, 152712, 999999 })]
    [InlineData(new[] { 152045, 999999 })]
    public async Task A_reorder_must_name_every_fund_once(int[] schemeCodes)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(152712);
        await client.AddMyFundAsync(152045);

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.ReorderMyFundsAsync(schemeCodes));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal([152712, 152045], (await client.GetMyFundsAsync()).Funds.Select(f => f.SchemeCode));
    }

    [Fact]
    public async Task Adding_a_fund_twice_keeps_one_entry()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", QuoteSnapApiFactory.ApiKey);

        using HttpResponseMessage first = await http.PutAsync("/api/my-funds/152045", null);
        using HttpResponseMessage second = await http.PutAsync("/api/my-funds/152045", null);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single((await client.GetMyFundsAsync()).Funds);
    }

    [Theory]
    [InlineData(152713)]
    [InlineData(999999)]
    public async Task Only_direct_plan_schemes_in_the_nav_file_can_be_added(int schemeCode)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.AddMyFundAsync(schemeCode));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Empty((await client.GetMyFundsAsync()).Funds);
    }

    [Fact]
    public async Task Adding_needs_navs_but_listing_does_not()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.AddMyFundAsync(152045));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        MyFundList list = await client.GetMyFundsAsync();
        Assert.Empty(list.Funds);
        Assert.Null(list.LoadedAt);
    }

    [Fact]
    public async Task The_list_outlives_nav_refreshes_even_when_a_scheme_leaves_the_file()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(152045);
        await client.AddMyFundAsync(152712);

        factory.NavFile = $"""
            {Header}
            Motilal Oswal Mutual Fund
            152712;INF247L01DH2;-;Motilal Oswal Nifty India Defence Index Fund;Direct Plan;Growth;12.0;30-Oct-2026
            """;
        await client.RefreshNavsAsync();

        MyFundList list = await client.GetMyFundsAsync();
        Assert.Equal([152045, 152712], list.Funds.Select(f => f.SchemeCode));
        Assert.Null(list.Funds[0].Latest);
        Assert.Equal("DSP Banking & Financial Services Fund", list.Funds[0].SchemeName);
        Assert.Equal(12.0m, list.Funds[1].Latest?.Nav);
    }
}

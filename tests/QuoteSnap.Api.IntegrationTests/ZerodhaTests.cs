using System.Net;
using System.Net.Http.Json;
using QuoteSnap.Api.Zerodha;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class ZerodhaTests
{
    private const string File = """
        Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date
        Open Ended Schemes(Equity Scheme - Sectoral/ Thematic)
        DSP Mutual Fund
        152045;INF740KA1TB3;-;DSP Banking & Financial Services Fund;Direct Plan;Growth;14.285;29-Oct-2026
        Open Ended Schemes(Other Scheme - Index Funds)
        Motilal Oswal Mutual Fund
        152712;INF247L01DH2;-;Motilal Oswal Nifty India Defence Index Fund;Direct Plan;Growth;11.6296;29-Oct-2026
        Open Ended Schemes(Debt Scheme - Liquid Fund)
        Parag Parikh Mutual Fund
        148001;INF879O01100;INF879O01118;Parag Parikh Liquid Fund;Direct Plan;IDCW Daily;1000.1;29-Oct-2026
        """;

    private static readonly ZerodhaAppCredentials App = new("kite-key", "kite-secret");

    private static string Holding(string isin, string fund, string folio, decimal quantity) =>
        $$"""{"folio":"{{folio}}","fund":"{{fund}}","tradingsymbol":"{{isin}}","average_price":10,"last_price":11,"last_price_date":"2026-10-29","pnl":1,"quantity":{{quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"pledged_quantity":0}""";

    [Fact]
    public async Task A_sync_makes_my_funds_match_the_holdings_without_logging_out_of_kite()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(152045);
        factory.KiteMfHoldings = $"[{Holding("INF247L01DH2", "MOTILAL OSWAL DEFENCE", "1", 10.5m)},{Holding("INF247L01DH2", "MOTILAL OSWAL DEFENCE", "2", 2.25m)},{Holding("inf879o01118", "PPFAS LIQUID IDCW", "3", 1m)},{Holding("INF000000000", "SOME REGULAR PLAN", "4", 3m)},{Holding("INF740KA1TB3", "DSP BANKING", "5", 0m)}]";

        ZerodhaMfSyncResult result = await client.SyncZerodhaMutualFundsAsync(App, "request-1");

        Assert.Equal("AB1234", result.UserId);
        Assert.Equal([152712, 148001], result.Added.Select(f => f.SchemeCode));
        Assert.Equal(12.75m, result.Added.Single(f => f.SchemeCode == 152712).Units);
        Assert.Equal([152045], result.Removed.Select(f => f.SchemeCode));
        Assert.Empty(result.Updated);
        Assert.Equal("INF000000000", Assert.Single(result.Unmatched).Isin);

        MyFundList list = await client.GetMyFundsAsync();
        Assert.Equal([152712, 148001], list.Funds.Select(f => f.SchemeCode));
        Assert.Equal([12.75m, 1m], list.Funds.Select(f => f.Units));

        (string Method, string PathAndQuery, string? Authorization, string? Body)[] calls = [.. factory.KiteCalls];
        // Revoking the token (DELETE /session/token) would also sign the browser out of Kite web.
        Assert.Equal(["POST", "GET"], calls.Select(c => c.Method));
        Assert.Contains("checksum=" + KiteClient.Checksum("kite-key", "request-1", "kite-secret"), calls[0].Body);
        Assert.Equal("token kite-key:kite-access-token", calls[1].Authorization);

        factory.KiteMfHoldings = $"[{Holding("INF247L01DH2", "MOTILAL OSWAL DEFENCE", "1", 20m)},{Holding("INF879O01118", "PPFAS LIQUID IDCW", "3", 1m)}]";
        ZerodhaMfSyncResult again = await client.SyncZerodhaMutualFundsAsync(App, "request-2");

        Assert.Empty(again.Added);
        Assert.Empty(again.Removed);
        Assert.Equal(20m, Assert.Single(again.Updated).Units);
        Assert.Equal(148001, Assert.Single(again.Unchanged).SchemeCode);
        Assert.Equal([20m, 1m], (await client.GetMyFundsAsync()).Funds.Select(f => f.Units));
    }

    [Fact]
    public async Task A_rejected_login_changes_nothing()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(152045);
        factory.KiteLoginResponse = (HttpStatusCode.Forbidden, """{"status":"error","message":"Token is invalid or has expired.","error_type":"TokenException"}""");

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SyncZerodhaMutualFundsAsync(App, "used-token"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(["POST"], factory.KiteCalls.Select(c => c.Method));
        Assert.Equal([152045], (await client.GetMyFundsAsync()).Funds.Select(f => f.SchemeCode));
    }

    [Fact]
    public async Task A_failed_holdings_read_changes_nothing()
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(152045);
        factory.KiteMfHoldings = "{}";

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SyncZerodhaMutualFundsAsync(App, "request-1"));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(["POST", "GET"], factory.KiteCalls.Select(c => c.Method));
        Assert.Equal([152045], (await client.GetMyFundsAsync()).Funds.Select(f => f.SchemeCode));
    }

    [Fact]
    public async Task Without_navs_the_request_token_is_not_used()
    {
        await using QuoteSnapApiFactory factory = new();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SyncZerodhaMutualFundsAsync(App, "request-1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Empty(factory.KiteCalls);
    }

    [Fact]
    public async Task The_login_url_carries_the_api_key_and_state()
    {
        await using QuoteSnapApiFactory factory = new();

        ZerodhaLoginUrl url = await factory.CreateQuoteSnapClient().GetZerodhaLoginUrlAsync("kite-key", "0123456789abcdef");

        Assert.Equal("https://kite.zerodha.com/connect/login?v=3&api_key=kite-key&redirect_params=state%3D0123456789abcdef", url.Url);
    }

    [Theory]
    [InlineData("GET", "/admin/zerodha/login-url?apiKey=kite-key&state=0123456789abcdef")]
    [InlineData("POST", "/admin/zerodha/mf-sync")]
    public async Task Zerodha_endpoints_reject_the_regular_api_key(string method, string path)
    {
        await using QuoteSnapApiFactory factory = new() { NavFile = File };
        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(QuoteSnapHeaders.ApiKey, QuoteSnapApiFactory.ApiKey);

        using HttpRequestMessage request = new(new HttpMethod(method), path) { Content = method == "POST" ? JsonContent.Create(new SyncZerodhaMutualFunds(App, "request-1")) : null };
        using HttpResponseMessage response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.KiteCalls);
    }
}

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Settings;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Tests;

public sealed class DashboardMarketStatusTests
{
    [Fact]
    public async Task Market_pill_follows_open_and_closed_responses()
    {
        using QuoteSnapConnection connection = new(new DesktopSettings(new Uri("http://localhost/"), null));
        DashboardViewModel dashboard = new(connection, new SymbolExclusions(connection));
        Queue<HttpResponseMessage> responses = new([Market(true, "OPEN"), Market(false, "CLOSED")]);
        using HttpClient http = new(new MarketHandler(() => responses.Dequeue()));
        QuoteSnapClient client = new(http, Options.Create(new QuoteSnapClientOptions()));

        await dashboard.ReadMarketAsync(client);
        Assert.Equal("Open", dashboard.MarketSummary);
        Assert.Equal(StatusLevel.Good, dashboard.MarketLevel);
        Assert.Contains("NSE cash market: OPEN", dashboard.MarketDetail);

        await dashboard.ReadMarketAsync(client);
        Assert.Equal("Closed", dashboard.MarketSummary);
        Assert.Equal(StatusLevel.Unknown, dashboard.MarketLevel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Errors_clear_the_previous_open_status_and_a_later_check_recovers(bool loginError)
    {
        using QuoteSnapConnection connection = new(new DesktopSettings(new Uri("http://localhost/"), null));
        DashboardViewModel dashboard = new(connection, new SymbolExclusions(connection));
        int calls = 0;
        using HttpClient http = new(new MarketHandler(() =>
        {
            if (++calls != 2)
                return Market(true, "OPEN");
            if (!loginError)
                throw new HttpRequestException("Network unavailable");
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent.Create(new { title = "Fyers re-authentication required", detail = "Log in again." }),
            };
        }));
        QuoteSnapClient client = new(http, Options.Create(new QuoteSnapClientOptions()));
        await dashboard.ReadMarketAsync(client);

        await dashboard.ReadMarketAsync(client);
        Assert.Equal("Unavailable", dashboard.MarketSummary);
        Assert.Equal(StatusLevel.Warning, dashboard.MarketLevel);
        Assert.Contains(loginError ? "Fyers re-authentication required" : "Network unavailable", dashboard.MarketDetail);

        await dashboard.ReadMarketAsync(client);
        Assert.Equal("Open", dashboard.MarketSummary);
        Assert.Equal(StatusLevel.Good, dashboard.MarketLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Missing_market_status_is_not_reported_as_closed(string? status)
    {
        using QuoteSnapConnection connection = new(new DesktopSettings(new Uri("http://localhost/"), null));
        DashboardViewModel dashboard = new(connection, new SymbolExclusions(connection));
        using HttpClient http = new(new MarketHandler(() => Market(false, status)));
        QuoteSnapClient client = new(http, Options.Create(new QuoteSnapClientOptions()));

        await dashboard.ReadMarketAsync(client);

        Assert.Equal("Unknown", dashboard.MarketSummary);
        Assert.Equal(StatusLevel.Warning, dashboard.MarketLevel);
        Assert.Contains("Fyers returned no status", dashboard.MarketDetail);
    }

    [Fact]
    public void Pushed_statuses_update_the_pill_and_pushed_errors_show_it_unavailable()
    {
        using QuoteSnapConnection connection = new(new DesktopSettings(new Uri("http://localhost/"), null));
        DashboardViewModel dashboard = new(connection, new SymbolExclusions(connection));

        dashboard.ShowMarket(new MarketStatus(true, "OPEN", DateTimeOffset.UtcNow));
        Assert.Equal(("Open", StatusLevel.Good), (dashboard.MarketSummary, dashboard.MarketLevel));

        dashboard.ShowMarket(new MarketStatus(false, null, DateTimeOffset.UtcNow, "Log in again."));
        Assert.Equal(("Unavailable", StatusLevel.Warning, "Log in again."), (dashboard.MarketSummary, dashboard.MarketLevel, dashboard.MarketDetail));
    }

    private static HttpResponseMessage Market(bool open, string? status) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new MarketStatus(open, status, DateTimeOffset.UtcNow)) };

    private sealed class MarketHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/market/status", request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond());
        }
    }
}

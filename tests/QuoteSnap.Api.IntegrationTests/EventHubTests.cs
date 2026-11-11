using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuoteSnap.Api.Events;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class EventHubTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task The_hub_needs_the_api_key()
    {
        await using QuoteSnapApiFactory factory = new(startWorker: false);
        HttpClient http = factory.CreateClient();
        string negotiate = QuoteSnapEvents.HubPath + "/negotiate?negotiateVersion=1";

        using HttpResponseMessage anonymous = await http.PostAsync(negotiate, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using HttpRequestMessage wrong = new(HttpMethod.Post, negotiate);
        wrong.Headers.Add(QuoteSnapHeaders.ApiKey, "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(wrong)).StatusCode);

        using HttpRequestMessage keyed = new(HttpMethod.Post, negotiate);
        keyed.Headers.Add(QuoteSnapHeaders.ApiKey, QuoteSnapApiFactory.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(keyed)).StatusCode);
    }

    [Fact]
    public async Task A_quote_request_is_pushed_as_it_progresses()
    {
        await using QuoteSnapApiFactory factory = new(delayBetweenBatchesMs: 50);
        await factory.SeedSessionAsync();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await using QuoteSnapHubClient hub = factory.CreateHubClient();
        ConcurrentQueue<QuoteRequestSummary> pushed = new();
        hub.QuoteRequestUpdated += pushed.Enqueue;
        using CancellationTokenSource timeout = new(WaitLimit);
        await hub.StartAsync(timeout.Token);

        QuoteRequestAccepted accepted = await client.SubmitAsync([.. Enumerable.Range(1, 120).Select(i => $"NSE:S{i}-EQ")], timeout.Token);
        List<QuoteRequestDetails> progress = [];
        QuoteRequestDetails details = await hub.WaitForCompletionAsync(client, accepted.RequestId, new SyncProgress<QuoteRequestDetails>(progress.Add), timeout.Token);

        Assert.Equal(QuoteRequestStatus.Completed, details.Status);
        Assert.True(progress.Count >= 2);
        await WaitForAsync(() => pushed.Any(p => p.Status.IsTerminal()), timeout.Token);
        List<QuoteRequestSummary> mine = [.. pushed.Where(p => p.RequestId == accepted.RequestId)];
        Assert.Equal(QuoteRequestStatus.Queued, mine[0].Status);
        Assert.Contains(mine, p => p.Status == QuoteRequestStatus.Running && p.SucceededCount > 0);
        Assert.Equal((QuoteRequestStatus.Completed, 120), (mine[^1].Status, mine[^1].SucceededCount));
    }

    [Fact]
    public async Task Market_status_changes_are_pushed_once()
    {
        await using QuoteSnapApiFactory factory = new(startWorker: false);
        factory.QuoteProvider.MarketStatus = "OPEN";
        MarketStatusMonitor monitor = factory.Services.GetServices<IHostedService>().OfType<MarketStatusMonitor>().Single();
        await using QuoteSnapHubClient hub = factory.CreateHubClient();
        ConcurrentQueue<MarketStatus> pushed = new();
        hub.MarketStatusChanged += pushed.Enqueue;
        using CancellationTokenSource timeout = new(WaitLimit);
        await hub.StartAsync(timeout.Token);
        HubPresence presence = factory.Services.GetRequiredService<HubPresence>();
        await WaitForAsync(() => presence.Count == 1, timeout.Token);

        await monitor.CheckAsync(timeout.Token);
        await monitor.CheckAsync(timeout.Token);
        await WaitForAsync(() => pushed.Count == 1, timeout.Token);
        Assert.Contains("Fyers", pushed.Single().Error);

        await factory.SeedSessionAsync();
        await monitor.CheckAsync(timeout.Token);
        await monitor.CheckAsync(timeout.Token);
        await WaitForAsync(() => pushed.Count == 2, timeout.Token);
        await Task.Delay(200, timeout.Token);

        MarketStatus open = Assert.Single(pushed.Skip(1));
        Assert.Equal((true, "OPEN", null), (open.IsOpen, open.Status, open.Error));
    }

    internal static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
            await Task.Delay(20, cancellationToken);
    }

    /// <summary>Reports on the caller's thread, unlike <see cref="Progress{T}"/>.</summary>
    internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

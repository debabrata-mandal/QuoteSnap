using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;

namespace QuoteSnap.Desktop.Tests;

public sealed class AutoPriceFetcherTests
{
    [Fact]
    public async Task Countdown_updates_each_second_and_checks_again_at_five_minutes()
    {
        using FetcherTest test = new((_, _) => Task.FromResult(Market(open: false)));
        test.Fetcher.SetEnabled(true);
        Assert.Contains("Next check in 05:00", await test.UntilAsync("NSE isn't trading"));

        Assert.Contains("Next check in 04:59", await test.AdvanceAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("Next check in 00:01", await test.AdvanceAsync(TimeSpan.FromSeconds(298)));
        Assert.Equal(1, test.Handler.MarketCalls);

        await test.AdvanceAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, test.Handler.MarketCalls);
        Assert.True(test.Fetcher.IsEnabled);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("network")]
    [InlineData("cancelled")]
    [InlineData("unexpected")]
    [InlineData("notification")]
    public async Task Failure_does_not_prevent_the_next_scheduled_fetch(string failure)
    {
        using FetcherTest test = new((call, _) =>
        {
            if (call > 1 || failure == "notification")
                return Task.FromResult(Market(open: true));
            return failure switch
            {
                "login" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { title = "Fyers re-authentication required" }) }),
                "network" => Task.FromException<HttpResponseMessage>(new HttpRequestException("Network unavailable")),
                "cancelled" => Task.FromException<HttpResponseMessage>(new TaskCanceledException()),
                _ => Task.FromException<HttpResponseMessage>(new InvalidOperationException("Unexpected failure")),
            };
        });
        int notifications = 0;
        test.Fetcher.PricesRequested += () =>
        {
            if (++notifications == 1 && failure == "notification")
                throw new InvalidOperationException("Notification failed");
        };
        DateTimeOffset started = test.Time.GetUtcNow();
        test.Fetcher.SetEnabled(true);
        string failed = await test.UntilAsync("Couldn't fetch live prices");
        Assert.Contains("Next check in", failed);

        await test.AdvanceAsync(started + TimeSpan.FromMinutes(5) - test.Time.GetUtcNow());
        await test.UntilAsync("Requested live prices");
        Assert.Equal(2, test.Handler.MarketCalls);
        Assert.True(test.Handler.QuoteCalls > 0);
    }

    [Fact]
    public async Task Stalled_request_is_bounded_even_when_it_ignores_cancellation()
    {
        TaskCompletionSource<HttpResponseMessage> stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using FetcherTest test = new((call, _) => call == 1 ? stalled.Task : Task.FromResult(Market(open: true)));
        DateTimeOffset started = test.Time.GetUtcNow();
        test.Fetcher.SetEnabled(true);
        await test.UntilAsync("Checking live prices");
        Assert.Contains("Next check in 04:59", await test.AdvanceAsync(TimeSpan.FromSeconds(1)));

        await test.AdvanceAsync(TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1));
        await test.UntilAsync("timed out after 2 minutes");
        await test.AdvanceAsync(started + TimeSpan.FromMinutes(5) - test.Time.GetUtcNow());
        await test.UntilAsync("Requested live prices");
        Assert.Equal(2, test.Handler.MarketCalls);
        Assert.Equal(1, test.Handler.QuoteCalls);
        stalled.SetResult(Market(open: true));
    }

    [Fact]
    public async Task Disabling_stops_countdown_and_reenabling_checks_immediately()
    {
        using FetcherTest test = new((_, _) => Task.FromResult(Market(open: true)));
        test.Fetcher.SetEnabled(true);
        await test.UntilAsync("Requested live prices");
        test.Fetcher.SetEnabled(false);
        test.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.False(test.Fetcher.IsEnabled);
        Assert.False(test.Updates.Reader.TryRead(out _));
        Assert.Equal(1, test.Handler.MarketCalls);

        test.Fetcher.SetEnabled(true);
        Assert.Contains("Next check in 05:00", await test.UntilAsync("Requested live prices"));
        Assert.Equal(2, test.Handler.MarketCalls);
        Assert.Equal("?reuseWithinSeconds=240", test.Handler.LastQuoteQuery);
    }

    private static HttpResponseMessage Market(bool open) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new MarketStatus(open, open ? "OPEN" : "CLOSED", DateTimeOffset.UtcNow)) };

    private sealed class FetcherTest : IDisposable
    {
        private readonly HttpClient _http;
        private string _last = "";

        public FetcherTest(Func<int, CancellationToken, Task<HttpResponseMessage>> market)
        {
            Handler = new TestHandler(market);
            _http = new HttpClient(Handler) { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
            QuoteSnapClient client = new(_http, Options.Create(new QuoteSnapClientOptions()));
            Fetcher = new AutoPriceFetcher(() => client, () => _http.BaseAddress!, Time);
            Fetcher.Checked += text => Updates.Writer.TryWrite(text);
        }

        public FakeTimeProvider Time { get; } = new();
        public TestHandler Handler { get; }
        public AutoPriceFetcher Fetcher { get; }
        public Channel<string> Updates { get; } = Channel.CreateUnbounded<string>();

        public async Task<string> AdvanceAsync(TimeSpan elapsed)
        {
            Time.Advance(elapsed);
            _last = await Updates.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            return _last;
        }

        public async Task<string> UntilAsync(string text)
        {
            for (int i = 0; i < 20; i++)
            {
                while (Updates.Reader.TryRead(out string? update))
                    _last = update;
                if (_last.Contains(text, StringComparison.Ordinal))
                    return _last;
                await Task.Delay(10);
                await AdvanceAsync(TimeSpan.FromSeconds(1));
            }

            Assert.Fail($"Expected '{text}', got '{_last}'.");
            return _last;
        }

        public void Dispose()
        {
            Fetcher.SetEnabled(false);
            _http.Dispose();
        }
    }

    private sealed class TestHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> market) : HttpMessageHandler
    {
        public int MarketCalls { get; private set; }
        public int QuoteCalls { get; private set; }
        public string? LastQuoteQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/market/status")
                return market(++MarketCalls, cancellationToken);

            QuoteCalls++;
            LastQuoteQuery = request.RequestUri.Query;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = JsonContent.Create(new QuoteRequestAccepted(Guid.NewGuid(), QuoteRequestStatus.Queued, 1, [], "/api/quote-requests/test", null)),
            });
        }
    }
}

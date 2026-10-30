using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class QuoteRequestTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Submitted_request_completes_with_results_in_caller_order()
    {
        await using QuoteSnapApiFactory factory = new();
        await factory.SeedSessionAsync();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        QuoteRequestAccepted accepted = await client.SubmitAsync(["nse:sbin-eq", "NSE:RELIANCE-EQ", "NSE:FAIL-EQ", "bad symbol", "NSE:SBIN-EQ"]);

        Assert.Equal(4, accepted.TotalSymbols);
        Assert.Equal(["BAD SYMBOL"], accepted.InvalidSymbols);
        Assert.Equal($"/api/quote-requests/{accepted.RequestId}", accepted.StatusUrl);
        Assert.InRange(accepted.RefreshTokenExpiresInDays ?? -1, 14, 15);

        using CancellationTokenSource timeout = new(WaitLimit);
        QuoteRequestDetails details = await client.WaitForCompletionAsync(accepted.RequestId, cancellationToken: timeout.Token);

        Assert.Equal(QuoteRequestStatus.CompletedWithErrors, details.Status);
        Assert.Equal(2, details.SucceededCount);
        Assert.Equal(2, details.FailedCount);
        Assert.NotNull(details.CompletedAt);
        Assert.Equal(["NSE:SBIN-EQ", "NSE:RELIANCE-EQ", "NSE:FAIL-EQ", "BAD SYMBOL"], details.Results.Select(r => r.Symbol));
        Assert.Equal(
            [QuoteResultStatus.Ok, QuoteResultStatus.Ok, QuoteResultStatus.Error, QuoteResultStatus.Invalid],
            details.Results.Select(r => r.Status));
        Assert.Equal(100.5m, details.Results[0].Ltp);
        Assert.Equal("invalid symbol", details.Results[2].Error);
    }

    [Fact]
    public async Task Requests_of_any_size_are_fetched_in_batches_of_at_most_50()
    {
        await using QuoteSnapApiFactory factory = new();
        await factory.SeedSessionAsync();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        List<string> symbols = Enumerable.Range(1, 1201).Select(i => $"NSE:STOCK{i}-EQ").ToList();

        using CancellationTokenSource timeout = new(WaitLimit);
        QuoteRequestDetails details = await client.FetchSnapshotAsync(symbols, cancellationToken: timeout.Token);

        Assert.Equal(QuoteRequestStatus.Completed, details.Status);
        Assert.Equal(1201, details.SucceededCount);
        Assert.Equal(symbols, details.Results.Select(r => r.Symbol));

        Assert.Equal([.. Enumerable.Repeat(50, 24), 1], factory.QuoteProvider.BatchSizes);
    }

    [Fact]
    public async Task Batches_are_spaced_by_the_configured_delay()
    {
        const int DelayMs = 150;
        await using QuoteSnapApiFactory factory = new(delayBetweenBatchesMs: DelayMs);
        await factory.SeedSessionAsync();
        List<string> symbols = Enumerable.Range(1, 120).Select(i => $"NSE:STOCK{i}-EQ").ToList();

        using CancellationTokenSource timeout = new(WaitLimit);
        await factory.CreateQuoteSnapClient().FetchSnapshotAsync(symbols, cancellationToken: timeout.Token);

        long[] calls = [.. factory.QuoteProvider.CallTimestamps];
        Assert.Equal(3, calls.Length);
        for (int i = 1; i < calls.Length; i++)
        {
            // Small allowance for timer resolution.
            TimeSpan gap = Stopwatch.GetElapsedTime(calls[i - 1], calls[i]);
            Assert.True(gap >= TimeSpan.FromMilliseconds(DelayMs - 20), $"Batch {i + 1} started only {gap.TotalMilliseconds:F0} ms after the previous one.");
        }
    }

    [Fact]
    public async Task Submitting_without_a_fyers_session_returns_503()
    {
        await using QuoteSnapApiFactory factory = new();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SubmitAsync(["NSE:SBIN-EQ"]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal("Fyers re-authentication required", ex.Title);
    }

    [Fact]
    public async Task Submitting_only_invalid_symbols_returns_400()
    {
        await using QuoteSnapApiFactory factory = new();
        await factory.SeedSessionAsync();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() =>
            factory.CreateQuoteSnapClient().SubmitAsync(["SBIN", "RELIANCE"]));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Unknown_request_returns_404()
    {
        await using QuoteSnapApiFactory factory = new();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() =>
            factory.CreateQuoteSnapClient().GetAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Pending_request_sends_retry_after()
    {
        await using QuoteSnapApiFactory factory = new(startWorker: false);
        await factory.SeedSessionAsync();
        QuoteRequestAccepted accepted = await factory.CreateQuoteSnapClient().SubmitAsync(["NSE:SBIN-EQ"]);

        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(QuoteSnapHeaders.ApiKey, QuoteSnapApiFactory.ApiKey);
        using HttpResponseMessage response = await http.GetAsync(accepted.StatusUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        QuoteRequestDetails? details = await response.Content.ReadFromJsonAsync<QuoteRequestDetails>();
        Assert.Equal(QuoteRequestStatus.Queued, details!.Status);
    }

    [Fact]
    public async Task List_returns_newest_first_in_pages()
    {
        await using QuoteSnapApiFactory factory = new(startWorker: false);
        await factory.SeedSessionAsync();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        List<Guid> ids = new();
        foreach (string symbol in new[] { "NSE:A-EQ", "NSE:B-EQ", "NSE:C-EQ" })
        {
            ids.Add((await client.SubmitAsync([symbol])).RequestId);
            await Task.Delay(5);
        }

        QuoteRequestPage first = await client.ListAsync(limit: 2);
        QuoteRequestPage second = await client.ListAsync(limit: 2, before: first.NextBefore);

        Assert.Equal([ids[2], ids[1]], first.Items.Select(i => i.RequestId));
        Assert.NotNull(first.NextBefore);
        Assert.Equal([ids[0]], second.Items.Select(i => i.RequestId));
        Assert.Null(second.NextBefore);
    }

    [Fact]
    public async Task Unfinished_requests_are_resumed_after_a_restart()
    {
        string dataDirectory = QuoteSnapApiFactory.NewDataDirectory();
        try
        {
            Guid requestId;
            await using (QuoteSnapApiFactory first = new(dataDirectory, startWorker: false))
            {
                await first.SeedSessionAsync();
                await using AsyncServiceScope scope = first.Services.CreateAsyncScope();
                QuoteRequestRepository repository = scope.ServiceProvider.GetRequiredService<QuoteRequestRepository>();
                QuoteRequest request = await repository.CreateAsync(
                    [new SymbolEntry("NSE:SBIN-EQ", true), new SymbolEntry("NSE:TCS-EQ", true)],
                    CancellationToken.None);
                await repository.TryStartAsync(request.Id, CancellationToken.None);
                requestId = request.Id;
            }

            await using QuoteSnapApiFactory second = new(dataDirectory);
            using CancellationTokenSource timeout = new(WaitLimit);
            QuoteRequestDetails details = await second.CreateQuoteSnapClient().WaitForCompletionAsync(requestId, cancellationToken: timeout.Token);

            Assert.Equal(QuoteRequestStatus.Completed, details.Status);
            Assert.Equal(2, details.SucceededCount);
        }
        finally
        {
            QuoteSnapApiFactory.TryDeleteDirectory(dataDirectory);
        }
    }
}

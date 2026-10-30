using System.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>Processes quote requests one at a time, fetching their batches sequentially.</summary>
public sealed class QuoteRequestWorker(
    QuoteRequestQueue queue,
    IServiceScopeFactory scopes,
    SessionService session,
    IOptions<FyersOptions> fyersOptions,
    TimeProvider time,
    ILogger<QuoteRequestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (Guid requestId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(requestId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Quote request {RequestId} failed unexpectedly", requestId);
                await TryFailAsync(requestId, $"Unexpected error: {ex.Message}", stoppingToken);
            }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        QuoteRequestRepository repository = scope.ServiceProvider.GetRequiredService<QuoteRequestRepository>();
        List<Guid> unfinished = await repository.GetUnfinishedIdsAsync(cancellationToken);
        if (unfinished.Count > 0)
            logger.LogInformation("Re-queueing {Count} unfinished quote requests", unfinished.Count);
        foreach (Guid id in unfinished)
            queue.Enqueue(id);
    }

    private async Task ProcessAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        QuoteRequestRepository repository = scope.ServiceProvider.GetRequiredService<QuoteRequestRepository>();
        IQuoteProvider provider = scope.ServiceProvider.GetRequiredService<IQuoteProvider>();

        if (!await repository.TryStartAsync(requestId, cancellationToken))
        {
            logger.LogDebug("Quote request {RequestId} skipped: it has already finished or was deleted", requestId);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        List<string> pending = await repository.GetPendingSymbolsAsync(requestId, cancellationToken);
        int batchSize = fyersOptions.Value.BatchSize;
        TimeSpan delay = TimeSpan.FromMilliseconds(fyersOptions.Value.DelayBetweenBatchesMs);
        string[][] batches = pending.Chunk(batchSize).ToArray();
        logger.LogInformation(
            "Quote request {RequestId}: fetching {SymbolCount} symbols from Fyers in {BatchCount} batches of up to {BatchSize}, {DelayMs:0} ms apart",
            requestId, pending.Count, batches.Length, batchSize, delay.TotalMilliseconds);

        string? error = null;
        int succeeded = 0, failed = 0;
        try
        {
            if (batches.Length > 0)
            {
                AccessTokenLease lease = await session.GetAccessTokenAsync(cancellationToken);
                for (int i = 0; i < batches.Length; i++)
                {
                    BatchInfo info = new(requestId, i + 1, batches.Length, batches[i].Length);
                    if (i > 0 && delay > TimeSpan.Zero)
                    {
                        logger.LogDebug(
                            "Quote request {RequestId}: waiting {DelayMs:0} ms before batch {Batch}/{BatchCount}",
                            requestId, delay.TotalMilliseconds, info.Number, info.Count);
                        await Task.Delay(delay, time, cancellationToken);
                    }

                    long batchStart = Stopwatch.GetTimestamp();
                    (IReadOnlyList<ProviderQuote> quotes, lease) = await FetchBatchAsync(provider, lease, batches[i], info, cancellationToken);
                    await repository.SaveBatchAsync(requestId, quotes, cancellationToken);

                    int ok = quotes.Count(q => q.IsOk);
                    succeeded += ok;
                    failed += quotes.Count - ok;
                    logger.LogInformation(
                        "Quote request {RequestId}: batch {Batch}/{BatchCount} ({SymbolCount} symbols) done in {ElapsedMs:0} ms: {Succeeded} ok, {Failed} failed",
                        requestId, info.Number, info.Count, info.Symbols, Stopwatch.GetElapsedTime(batchStart).TotalMilliseconds, ok, quotes.Count - ok);
                }
            }
        }
        catch (FyersReloginRequiredException ex)
        {
            logger.LogWarning(
                "Quote request {RequestId}: Fyers needs a new login; the {Remaining} remaining symbols are marked failed",
                requestId, pending.Count - succeeded - failed);
            error = ex.Message;
            await repository.FailPendingAsync(requestId, ex.Message, cancellationToken);
        }

        QuoteRequestStatus status = await repository.CompleteAsync(requestId, error, cancellationToken);
        logger.LogInformation(
            "Quote request {RequestId} finished as {Status} in {ElapsedMs:0} ms: {Succeeded} ok, {Failed} failed of {SymbolCount} fetched",
            requestId, status, Stopwatch.GetElapsedTime(start).TotalMilliseconds, succeeded, failed, pending.Count);
    }

    private sealed record BatchInfo(Guid RequestId, int Number, int Count, int Symbols);

    private async Task<(IReadOnlyList<ProviderQuote> Quotes, AccessTokenLease Lease)> FetchBatchAsync(
        IQuoteProvider provider,
        AccessTokenLease lease,
        IReadOnlyList<string> batch,
        BatchInfo info,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return (await provider.GetQuotesAsync(lease.AppId, lease.Token, batch, cancellationToken), lease);
            }
            catch (FyersAuthException ex) when (attempt == 0)
            {
                logger.LogInformation(
                    "Quote request {RequestId}: Fyers rejected the access token on batch {Batch}/{BatchCount} ({Reason}); refreshing it and retrying",
                    info.RequestId, info.Number, info.Count, ex.Message);
                lease = await session.RefreshAsync(lease.Version, cancellationToken);
            }
            catch (FyersAuthException ex)
            {
                logger.LogWarning(
                    "Quote request {RequestId}: Fyers rejected a freshly refreshed access token on batch {Batch}/{BatchCount}: {Reason}",
                    info.RequestId, info.Number, info.Count, ex.Message);
                throw new FyersReloginRequiredException();
            }
            catch (Exception ex) when (IsFetchFailure(ex, cancellationToken))
            {
                logger.LogWarning(
                    "Quote request {RequestId}: batch {Batch}/{BatchCount} failed ({ErrorType}: {Error}); its {SymbolCount} symbols are marked failed",
                    info.RequestId, info.Number, info.Count, ex.GetType().Name, ex.Message, batch.Count);
                List<ProviderQuote> failures = batch.Select(s => ProviderQuote.Failure(s, $"Fetch failed: {ex.Message}")).ToList();
                return (failures, lease);
            }
        }
    }

    private static bool IsFetchFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is FyersApiException or HttpRequestException or ExecutionRejectedException
        || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private async Task TryFailAsync(Guid requestId, string error, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            QuoteRequestRepository repository = scope.ServiceProvider.GetRequiredService<QuoteRequestRepository>();
            await repository.FailPendingAsync(requestId, error, cancellationToken);
            await repository.CompleteAsync(requestId, error, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not mark quote request {RequestId} as failed", requestId);
        }
    }
}

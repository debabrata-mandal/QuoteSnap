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
            return;

        List<string> pending = await repository.GetPendingSymbolsAsync(requestId, cancellationToken);
        logger.LogInformation("Processing quote request {RequestId}: {Count} symbols", requestId, pending.Count);

        string? error = null;
        try
        {
            if (pending.Count > 0)
            {
                AccessTokenLease lease = await session.GetAccessTokenAsync(cancellationToken);
                string[][] batches = pending.Chunk(fyersOptions.Value.BatchSize).ToArray();
                TimeSpan delay = TimeSpan.FromMilliseconds(fyersOptions.Value.DelayBetweenBatchesMs);
                for (int i = 0; i < batches.Length; i++)
                {
                    if (i > 0 && delay > TimeSpan.Zero)
                        await Task.Delay(delay, time, cancellationToken);

                    (IReadOnlyList<ProviderQuote> quotes, lease) = await FetchBatchAsync(provider, lease, batches[i], cancellationToken);
                    await repository.SaveBatchAsync(requestId, quotes, cancellationToken);
                }
            }
        }
        catch (FyersReloginRequiredException ex)
        {
            error = ex.Message;
            await repository.FailPendingAsync(requestId, ex.Message, cancellationToken);
        }

        QuoteRequestStatus status = await repository.CompleteAsync(requestId, error, cancellationToken);
        logger.LogInformation("Quote request {RequestId} finished: {Status}", requestId, status);
    }

    private async Task<(IReadOnlyList<ProviderQuote> Quotes, AccessTokenLease Lease)> FetchBatchAsync(
        IQuoteProvider provider,
        AccessTokenLease lease,
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return (await provider.GetQuotesAsync(lease.AppId, lease.Token, batch, cancellationToken), lease);
            }
            catch (FyersAuthException) when (attempt == 0)
            {
                lease = await session.RefreshAsync(lease.Version, cancellationToken);
            }
            catch (FyersAuthException ex)
            {
                logger.LogWarning("Fyers rejected a freshly refreshed access token: {Reason}", ex.Message);
                throw new FyersReloginRequiredException();
            }
            catch (Exception ex) when (IsFetchFailure(ex, cancellationToken))
            {
                logger.LogWarning(ex, "Fetching a batch of {Count} quotes failed", batch.Count);
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

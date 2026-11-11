using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Events;

/// <summary>
/// Once a minute, while a client is connected, reads NSE's market status through <see cref="MarketStatusCache"/> and
/// pushes it when it, or the error reading it, changed. Fyers has no market status push, so this is the only poller.
/// </summary>
public sealed class MarketStatusMonitor(MarketStatusCache cache, SessionService session, EventPublisher events, HubPresence presence, IServiceScopeFactory scopes, TimeProvider time, ILogger<MarketStatusMonitor> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = MarketStatusCache.Lifetime;

    private MarketStatus? _last;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await CheckAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One check; public so tests can run it without waiting for the timer.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (presence.Count == 0)
        {
            // Clients read the status when they connect, so nothing is owed.
            _last = null;
            return;
        }

        MarketStatus status;
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            IQuoteProvider provider = scope.ServiceProvider.GetRequiredService<IQuoteProvider>();
            status = await cache.GetAsync(ct => StatusEndpoints.ReadMarketStatusAsync(session, provider, ct), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            string error = ex switch
            {
                FyersReloginRequiredException => ex.Message,
                FyersAuthException => FyersReloginRequiredException.DefaultMessage,
                _ => $"Fyers couldn't say whether the market is open: {ex.Message}",
            };
            if (_last?.Error != error)
                logger.LogWarning("Couldn't read the market status from Fyers: {Error}", ex.Message);
            status = new MarketStatus(false, null, time.GetUtcNow(), error);
        }

        if (_last is not null && _last.IsOpen == status.IsOpen && _last.Status == status.Status && _last.Error == status.Error)
            return;
        _last = status;
        await events.MarketStatusChangedAsync(status);
    }
}

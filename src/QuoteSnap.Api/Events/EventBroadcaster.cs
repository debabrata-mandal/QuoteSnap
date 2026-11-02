using QuoteSnap.Api.Live;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Events;

/// <summary>
/// Once a second, while a client is connected, pushes the live feed's status when it changed, the quotes ticked since
/// the last push, and the NAV estimates when either those quotes or what the estimates depend on changed.
/// </summary>
public sealed class EventBroadcaster(LiveQuoteStore store, EventPublisher events, HubPresence presence, IServiceScopeFactory scopes, TimeProvider time, ILogger<EventBroadcaster> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private LiveFeedStatus? _lastStatus;
    private long _quotesVersion;
    private EstimateBasis? _basis;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await BroadcastAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Couldn't push the live feed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One push; public so tests can run it without waiting for the timer.</summary>
    public async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        if (presence.Count == 0)
        {
            // Whoever connects next loads everything once, so nothing is owed.
            _lastStatus = null;
            _basis = null;
            _quotesVersion = store.GetChangedSince(long.MaxValue).Version;
            return;
        }

        LiveFeedStatus status = store.GetStatus();
        if (_lastStatus is null || !SameStatus(_lastStatus, status))
        {
            _lastStatus = status;
            await events.LiveFeedStatusChangedAsync(status);
        }

        (IReadOnlyList<LiveQuote> changed, long version) = store.GetChangedSince(_quotesVersion);
        _quotesVersion = version;
        if (changed.Count > 0 && logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Broadcast: {Count} quote(s) ticked since the last push, e.g. {Sample}", changed.Count, string.Join(", ", changed.Take(5).Select(q => $"{q.Symbol} {q.Ltp} ({q.ChangePercent:+0.00;-0.00}%)")));
        if (changed.Count > 0)
            await events.LiveQuotesUpdatedAsync(new LiveQuoteList(status.State, status.LastTickAt, changed));

        bool stale = events.TakeEstimatesStale() || _basis is null;
        if (stale)
        {
            using IServiceScope scope = scopes.CreateScope();
            _basis = await EstimateBasis.LoadAsync(scope.ServiceProvider, cancellationToken);
            logger.LogDebug("Broadcast: reloaded the estimate basis ({Funds} funds)", _basis.Funds.Count);
        }

        if (changed.Count > 0 || stale)
            await events.EstimatesUpdatedAsync(_basis!.Estimate(store.GetQuotes().Quotes));
    }

    // Every tick moves LastTickAt; that reaches clients with the quotes instead.
    private static bool SameStatus(LiveFeedStatus a, LiveFeedStatus b) =>
        a.State == b.State && a.QuoteCount == b.QuoteCount && a.ConnectedAt == b.ConnectedAt && a.LastError == b.LastError && a.LastErrorAt == b.LastErrorAt && a.Symbols.SequenceEqual(b.Symbols, StringComparer.OrdinalIgnoreCase);
}

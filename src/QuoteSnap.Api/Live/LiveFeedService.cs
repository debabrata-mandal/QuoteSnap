using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Live;

/// <summary>
/// Streams LTP and the day's change of the listed funds' top holdings, ranked as on the dashboard, and of the
/// <see cref="MarketIndex.All">market indices</see> over one Fyers
/// Data Socket. Ranks again when <see cref="LiveFeedSignal"/> fires and changes only the symbols
/// that moved in or out; opens a new socket when the access token changes or the SDK gives up reconnecting.
/// </summary>
public sealed class LiveFeedService(LiveQuoteStore store, LiveFeedSignal signal, ILiveFeedConnectionFactory connections, SessionService sessions, IServiceScopeFactory scopes, IOptions<LiveQuotesOptions> options, TimeProvider time, ILogger<LiveFeedService> logger) : BackgroundService
{
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    // Unsubscribing first keeps the socket under Fyers' symbol limit, but the SDK sends both without waiting.
    private static readonly TimeSpan SwapDelay = TimeSpan.FromSeconds(2);

    private Feed? _feed;
    private List<string>? _wanted;
    private bool _rerank = true;
    private DateOnly _rankedDay;
    private DateTimeOffset _retryAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LiveQuotesOptions settings = options.Value;
        if (!settings.Enabled)
        {
            store.SetState(LiveFeedState.Disabled);
            logger.LogInformation("Live quotes are disabled");
            return;
        }

        try
        {
            while (true)
            {
                try
                {
                    await CheckAsync(settings, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Live feed check failed");
                    store.SetError(ex.Message);
                    Close(LiveFeedState.Error);
                    _rerank = true;
                    _retryAt = time.GetUtcNow() + RetryDelay;
                }

                if (await signal.WaitAsync(TimeSpan.FromSeconds(settings.CheckIntervalSeconds), time, stoppingToken))
                    _rerank = true;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _feed?.Close();
            _feed = null;
        }
    }

    private async Task CheckAsync(LiveQuotesOptions settings, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if ((await sessions.GetInfoAsync(cancellationToken)).State == FyersAuthState.ReloginRequired)
        {
            Close(LiveFeedState.NoSession);
            return;
        }

        AccessTokenLease lease;
        try
        {
            lease = await sessions.GetAccessTokenAsync(cancellationToken);
        }
        catch (FyersReloginRequiredException)
        {
            Close(LiveFeedState.NoSession);
            return;
        }

        // Yesterday's exclusions lapse at midnight.
        if (_rerank || _wanted is null || DateOnly.FromDateTime(time.GetLocalNow().DateTime) != _rankedDay)
        {
            _wanted = await RankAsync(settings.SymbolCount, cancellationToken);
            _rerank = false;
        }

        if (_wanted.Count == 0)
        {
            Close(LiveFeedState.NoHoldings);
            return;
        }

        if (_feed is { } stale && (stale.Failed is not null || stale.TokenVersion != lease.Version))
        {
            logger.LogInformation("Live feed reconnecting: {Reason}", stale.Failed ?? "the access token changed");
            Close(LiveFeedState.Connecting);
        }

        if (_feed is null)
        {
            if (now < _retryAt)
                return;
            _feed = await ConnectAsync(lease, cancellationToken);
            if (_feed is null)
            {
                _retryAt = now + RetryDelay;
                return;
            }
        }

        await SyncAsync(_feed, _wanted, cancellationToken);
    }

    private async Task<List<string>> RankAsync(int count, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Dictionary<int, List<FundHoldingRecord>> portfolios = await scope.ServiceProvider.GetRequiredService<HoldingRepository>().GetLatestHoldingsAsync(cancellationToken);
        SymbolExclusionRepository exclusions = scope.ServiceProvider.GetRequiredService<SymbolExclusionRepository>();
        _rankedDay = exclusions.Today;
        HashSet<string> excluded = await exclusions.GetTodaySymbolsAsync(cancellationToken);
        // Excluded symbols give up their places to the next holdings by weight.
        List<string> holdings = [.. TopHoldings.Rank(portfolios, null, int.MaxValue).Select(h => h.Symbol).Where(s => !excluded.Contains(s)).Take(count)];
        return holdings.Count == 0 ? holdings : [.. holdings, .. MarketIndex.All.Select(i => i.Symbol)];
    }

    private async Task<Feed?> ConnectAsync(AccessTokenLease lease, CancellationToken cancellationToken)
    {
        if (store.SetState(LiveFeedState.Connecting))
            logger.LogInformation("Live feed connecting to Fyers");

        Feed feed = new(store, signal, logger, lease.Version);
        try
        {
            feed.Connection = connections.Create(lease.AppId, lease.Token, feed);
            // The SDK connects synchronously.
            await Task.Run(feed.Connection.Connect, cancellationToken);
            await feed.Opened.Task.WaitAsync(OpenTimeout, time, cancellationToken);
            logger.LogInformation("Live feed connected");
            return feed;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            string error = ex is TimeoutException ? $"The socket didn't open within {OpenTimeout.TotalSeconds:0} seconds. {feed.LastError}".Trim() : ex.Message;
            logger.LogWarning("Live feed couldn't connect: {Error}", error);
            feed.Close();
            store.SetError(error);
            store.SetState(LiveFeedState.Error);
            return null;
        }
    }

    private async Task SyncAsync(Feed feed, List<string> wanted, CancellationToken cancellationToken)
    {
        HashSet<string> wantedSet = new(wanted, StringComparer.OrdinalIgnoreCase);
        List<string> remove = [.. feed.Subscribed.Where(s => !wantedSet.Contains(s))];
        List<string> add = [.. wanted.Where(s => !feed.Subscribed.Contains(s))];
        store.SetSymbols(wanted);
        if (remove.Count == 0 && add.Count == 0)
            return;

        if (remove.Count > 0)
        {
            feed.Connection.Unsubscribe(remove);
            feed.Subscribed.ExceptWith(remove);
            if (add.Count > 0)
                await Task.Delay(SwapDelay, time, cancellationToken);
        }

        if (add.Count > 0)
        {
            feed.Connection.Subscribe(add);
            feed.Subscribed.UnionWith(add);
        }

        logger.LogInformation("Live feed streams {Count} symbols: {Added} added, {Removed} removed", feed.Subscribed.Count, add.Count, remove.Count);
    }

    /// <summary>Closes the socket, if open, and keeps the last quotes.</summary>
    private void Close(LiveFeedState state)
    {
        if (_feed is not null)
        {
            _feed.Close();
            _feed = null;
            logger.LogInformation("Live feed closed");
        }

        if (store.SetState(state))
            logger.LogInformation("Live feed is {State}", state);
    }

    /// <summary>One socket's listener; events from a socket that has been closed are ignored.</summary>
    private sealed class Feed(LiveQuoteStore store, LiveFeedSignal signal, ILogger logger, int tokenVersion) : ILiveFeedListener
    {
        private volatile bool _closed;
        private volatile string? _failed;
        private volatile string? _lastError;

        public ILiveFeedConnection Connection { get; set; } = null!;

        public int TokenVersion { get; } = tokenVersion;

        public HashSet<string> Subscribed { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Why the socket is dead, once the SDK has given up reconnecting.</summary>
        public string? Failed => _failed;

        public string? LastError => _lastError;

        public void Close()
        {
            _closed = true;
            Connection?.Dispose();
        }

        public void OnOpened()
        {
            if (_closed)
                return;
            store.SetConnected();
            if (!Opened.TrySetResult())
                logger.LogInformation("Live feed reconnected");
        }

        public void OnTick(LiveTick tick)
        {
            if (_closed)
                return;
            logger.LogTrace("Live tick {Symbol} LTP {Ltp} change {ChangePercent}%", tick.Symbol, tick.Ltp, tick.ChangePercent);
            store.Record(tick);
        }

        public void OnError(string message, bool fatal)
        {
            if (_closed)
                return;
            logger.LogWarning("Live feed error: {Error}", message);
            _lastError = message;
            store.SetError(message);
            if (!fatal)
                return;
            _failed = message;
            store.SetState(LiveFeedState.Error);
            signal.Notify();
        }

        public void OnClosed(string reason)
        {
            if (_closed || !Opened.Task.IsCompleted)
                return;
            logger.LogWarning("Live feed disconnected: {Reason}", reason);
            store.SetState(LiveFeedState.Reconnecting);
        }
    }
}

using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Client;

/// <summary>
/// The connection to the service's event hub. It keeps reconnecting until disposed; <see cref="Connected"/> fires after
/// every (re)connection, when anything pushed in between was missed and should be read again.
/// Events are raised on a thread-pool thread.
/// </summary>
public sealed class QuoteSnapHubClient : IAsyncDisposable
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>How long a wait goes without a push before reading the state anyway.</summary>
    public static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(30);

    private readonly HubConnection _connection;
    private readonly QuoteSnapClientOptions _options;
    private readonly CancellationTokenSource _disposed = new();
    private readonly Lock _lock = new();
    private Task? _starting;

    public QuoteSnapHubClient(QuoteSnapClientOptions options, Action<HttpConnectionOptions>? configure = null)
    {
        _options = options;
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(options.BaseAddress, QuoteSnapEvents.HubPath.TrimStart('/')), o =>
            {
                if (!string.IsNullOrEmpty(options.ApiKey))
                    o.Headers[QuoteSnapHeaders.ApiKey] = options.ApiKey;
                configure?.Invoke(o);
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        _connection.On<QuoteRequestSummary>(nameof(IQuoteSnapEvents.QuoteRequestUpdated), r => QuoteRequestUpdated?.Invoke(r));
        _connection.On<HoldingFetch>(nameof(IQuoteSnapEvents.HoldingFetchUpdated), f => HoldingFetchUpdated?.Invoke(f));
        _connection.On<LiveFeedStatus>(nameof(IQuoteSnapEvents.LiveFeedStatusChanged), s => LiveFeedStatusChanged?.Invoke(s));
        _connection.On<LiveQuoteList>(nameof(IQuoteSnapEvents.LiveQuotesUpdated), q => LiveQuotesUpdated?.Invoke(q));
        _connection.On<NavEstimateList>(nameof(IQuoteSnapEvents.EstimatesUpdated), e => EstimatesUpdated?.Invoke(e));
        _connection.On<ServerChange>(nameof(IQuoteSnapEvents.Changed), c => Changed?.Invoke(c));

        _connection.Reconnecting += _ => RaiseStateChanged();
        _connection.Reconnected += _ =>
        {
            StateChanged?.Invoke();
            Connected?.Invoke();
            return Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            StateChanged?.Invoke();
            if (!_disposed.IsCancellationRequested)
                Start();
            return Task.CompletedTask;
        };
    }

    public event Action<QuoteRequestSummary>? QuoteRequestUpdated;

    public event Action<HoldingFetch>? HoldingFetchUpdated;

    public event Action<LiveFeedStatus>? LiveFeedStatusChanged;

    public event Action<LiveQuoteList>? LiveQuotesUpdated;

    public event Action<NavEstimateList>? EstimatesUpdated;

    public event Action<ServerChange>? Changed;

    /// <summary>After the first connection and every reconnection.</summary>
    public event Action? Connected;

    public event Action? StateChanged;

    public HubConnectionState State => _connection.State;

    /// <summary>Starts connecting in the background, retrying until it succeeds; does nothing if already connecting.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_starting is null || _starting.IsCompleted)
                _starting = ConnectAsync(_disposed.Token);
        }
    }

    /// <summary>Starts connecting if needed and waits until connected.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Start();
        Task starting;
        lock (_lock)
            starting = _starting!;
        return starting.WaitAsync(cancellationToken);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; !cancellationToken.IsCancellationRequested; attempt++)
        {
            if (_connection.State != HubConnectionState.Disconnected)
                return;
            try
            {
                await Task.Delay(RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)], cancellationToken);
                await _connection.StartAsync(cancellationToken);
                StateChanged?.Invoke();
                Connected?.Invoke();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                StateChanged?.Invoke();
            }
        }
    }

    /// <summary>Waits for a quote request to finish, reading it again whenever the hub says it moved on.</summary>
    public Task<QuoteRequestDetails> WaitForCompletionAsync(QuoteSnapClient client, Guid requestId, IProgress<QuoteRequestDetails>? progress = null, CancellationToken cancellationToken = default) =>
        WaitAsync<QuoteRequestSummary, QuoteRequestDetails>(h => QuoteRequestUpdated += h, h => QuoteRequestUpdated -= h, r => r.RequestId == requestId, ct => client.GetAsync(requestId, ct), d => d.Status.IsTerminal(), progress, cancellationToken);

    /// <summary>Waits for a holdings fetch to finish, reading it again whenever the hub says it moved on.</summary>
    public Task<HoldingFetch> WaitForHoldingFetchAsync(QuoteSnapClient client, Guid fetchId, IProgress<HoldingFetch>? progress = null, CancellationToken cancellationToken = default) =>
        WaitAsync<HoldingFetch, HoldingFetch>(h => HoldingFetchUpdated += h, h => HoldingFetchUpdated -= h, f => f.Id == fetchId, ct => client.GetHoldingFetchAsync(fetchId, ct), f => f.Status.IsTerminal(), progress, cancellationToken);

    private async Task<TResult> WaitAsync<TEvent, TResult>(Action<Action<TEvent>> subscribe, Action<Action<TEvent>> unsubscribe, Func<TEvent, bool> matches, Func<CancellationToken, Task<TResult>> read, Func<TResult, bool> finished, IProgress<TResult>? progress, CancellationToken cancellationToken)
    {
        Channel<bool> signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        void OnEvent(TEvent e)
        {
            if (matches(e))
                signal.Writer.TryWrite(true);
        }

        void OnConnected() => signal.Writer.TryWrite(true);

        // Subscribed before the first read, so a push in between isn't missed.
        subscribe(OnEvent);
        Connected += OnConnected;
        try
        {
            while (true)
            {
                TResult result = await read(cancellationToken);
                progress?.Report(result);
                if (finished(result))
                    return result;

                TimeSpan fallback = State == HubConnectionState.Connected ? FallbackPollInterval : _options.DefaultPollInterval;
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(fallback);
                try
                {
                    await signal.Reader.ReadAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }
        }
        finally
        {
            unsubscribe(OnEvent);
            Connected -= OnConnected;
        }
    }

    private Task RaiseStateChanged()
    {
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed.IsCancellationRequested)
            return;
        await _disposed.CancelAsync();
        await _connection.DisposeAsync();
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => RetryDelays[(int)Math.Min(retryContext.PreviousRetryCount, RetryDelays.Length - 1)];
    }
}

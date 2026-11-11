using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Settings;

namespace QuoteSnap.Desktop.Services;

/// <summary>
/// The client and event hub for the current settings; both are replaced when the settings change.
/// Hub events are raised on the thread that created the connection (the UI thread).
/// </summary>
public sealed class QuoteSnapConnection : IDisposable
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private HttpClient _http;
    private QuoteSnapHubClient _hub;
    private bool _started;

    public QuoteSnapConnection(DesktopSettings settings)
    {
        (_http, Client) = Create(settings);
        _hub = CreateHub(settings);
        Settings = settings;
    }

    public event EventHandler? Changed;

    public event Action<QuoteRequestSummary>? QuoteRequestUpdated;

    public event Action<HoldingFetch>? HoldingFetchUpdated;

    public event Action<LiveFeedStatus>? LiveFeedStatusChanged;

    public event Action<LiveQuoteList>? LiveQuotesUpdated;

    public event Action<NavEstimateList>? EstimatesUpdated;

    public event Action<MarketStatus>? MarketStatusChanged;

    public event Action<ServerChange>? ServerChanged;

    /// <summary>After every (re)connection of the hub; anything pushed while disconnected should be read again.</summary>
    public event Action? HubConnected;

    public event Action? HubStateChanged;

    public DesktopSettings Settings { get; private set; }

    public QuoteSnapClient Client { get; private set; }

    public HubConnectionState HubState => _hub.State;

    /// <summary>Starts connecting the hub; it keeps reconnecting until the settings change or the app exits.</summary>
    public void Start()
    {
        _started = true;
        _hub.Start();
    }

    public void Apply(DesktopSettings settings)
    {
        HttpClient previous = _http;
        QuoteSnapHubClient previousHub = _hub;
        (_http, Client) = Create(settings);
        _hub = CreateHub(settings);
        Settings = settings;
        previous.Dispose();
        _ = previousHub.DisposeAsync().AsTask();
        if (_started)
            _hub.Start();
        Changed?.Invoke(this, EventArgs.Empty);
        HubStateChanged?.Invoke();
    }

    public Task<QuoteRequestDetails> WaitForCompletionAsync(Guid requestId, IProgress<QuoteRequestDetails>? progress = null, CancellationToken cancellationToken = default) =>
        _hub.WaitForCompletionAsync(Client, requestId, progress, cancellationToken);

    public Task<HoldingFetch> WaitForHoldingFetchAsync(Guid fetchId, IProgress<HoldingFetch>? progress = null, CancellationToken cancellationToken = default) =>
        _hub.WaitForHoldingFetchAsync(Client, fetchId, progress, cancellationToken);

    public static (HttpClient Http, QuoteSnapClient Client) Create(DesktopSettings settings)
    {
        HttpClient http = new() { BaseAddress = settings.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
        QuoteSnapClient client = new(http, Options.Create(ClientOptions(settings)));
        return (http, client);
    }

    private static QuoteSnapClientOptions ClientOptions(DesktopSettings settings) => new()
    {
        BaseAddress = settings.BaseAddress,
        ApiKey = settings.ApiKey,
        AdminApiKey = settings.AdminApiKey,
    };

    private QuoteSnapHubClient CreateHub(DesktopSettings settings)
    {
        QuoteSnapHubClient hub = new(ClientOptions(settings));
        // Only the current hub's events get through; a replaced hub may still be winding down.
        hub.QuoteRequestUpdated += r => Post(hub, () => QuoteRequestUpdated?.Invoke(r));
        hub.HoldingFetchUpdated += f => Post(hub, () => HoldingFetchUpdated?.Invoke(f));
        hub.LiveFeedStatusChanged += s => Post(hub, () => LiveFeedStatusChanged?.Invoke(s));
        hub.LiveQuotesUpdated += q => Post(hub, () => LiveQuotesUpdated?.Invoke(q));
        hub.EstimatesUpdated += e => Post(hub, () => EstimatesUpdated?.Invoke(e));
        hub.MarketStatusChanged += s => Post(hub, () => MarketStatusChanged?.Invoke(s));
        hub.Changed += c => Post(hub, () => ServerChanged?.Invoke(c));
        hub.Connected += () => Post(hub, () => HubConnected?.Invoke());
        hub.StateChanged += () => Post(hub, () => HubStateChanged?.Invoke());
        return hub;
    }

    private void Post(QuoteSnapHubClient source, Action raise)
    {
        void RaiseIfCurrent()
        {
            if (ReferenceEquals(source, _hub))
                raise();
        }

        if (_context is null)
            RaiseIfCurrent();
        else
            _context.Post(_ => RaiseIfCurrent(), null);
    }

    public void Dispose()
    {
        _http.Dispose();
        // Bounded, so a hub stuck closing can't hold up the app's exit.
        QuoteSnapHubClient hub = _hub;
        Task.Run(() => hub.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
    }
}

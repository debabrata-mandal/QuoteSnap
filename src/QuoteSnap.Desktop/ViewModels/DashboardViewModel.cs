using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.AspNetCore.SignalR.Client;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private const int TopCount = 10;

    /// <summary>How many tiles the market strip has, for the dividers between them.</summary>
    public static int IndexCount => MarketIndex.All.Count;
    private const int TodayPageSize = 50;
    private static readonly TimeSpan LoginExpiryWarning = TimeSpan.FromDays(2);
    private static readonly TimeSpan LoginLifetime = TimeSpan.FromDays(15);

    private readonly QuoteSnapConnection _connection;
    private readonly SymbolExclusions _exclusions;
    private QuoteRequestPage? _requests;
    private bool _online;
    private bool _refreshAgain;
    private HubConnectionState _hubState;
    private LiveFeedStatus? _liveStatus;
    private TopHoldingList? _top;
    private Dictionary<string, decimal?> _dayChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveQuote> _indexQuotes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads everything once, then follows the pushes from the API's event hub; there is no polling.</summary>
    public DashboardViewModel(QuoteSnapConnection connection, SymbolExclusions exclusions)
    {
        _connection = connection;
        _exclusions = exclusions;
        _exclusions.Changed += (_, _) =>
        {
            if (_requests is { } page)
                ShowRequests(page);
        };
        _hubState = connection.HubState;
        connection.Changed += async (_, _) =>
        {
            OnPropertyChanged(nameof(ConnectionHost));
            await RefreshIfIdleAsync();
        };
        connection.HubConnected += async () => await RefreshIfIdleAsync();
        connection.HubStateChanged += async () => await OnHubStateChangedAsync();
        connection.ServerChanged += async _ => await RefreshIfIdleAsync();
        connection.QuoteRequestUpdated += async r =>
        {
            if (r.Status == QuoteRequestStatus.Queued || r.Status.IsTerminal())
                await RefreshIfIdleAsync();
        };
        connection.LiveFeedStatusChanged += ShowLiveFeed;
        connection.LiveQuotesUpdated += ShowLiveQuotes;
        connection.EstimatesUpdated += e =>
        {
            Portfolio = PortfolioMove.From(e);
            LastChecked = DateTimeOffset.Now;
        };
    }

    public string ConnectionHost => _connection.Settings.BaseAddress.Authority;

    [ObservableProperty]
    public partial StatusLevel ApiLevel { get; set; }

    [ObservableProperty]
    public partial string ApiSummary { get; set; } = "Checking";

    [ObservableProperty]
    public partial string ApiValue { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string? ApiDetail { get; set; }

    [ObservableProperty]
    public partial string? ApiDetailTooltip { get; set; }

    [ObservableProperty]
    public partial StatusLevel FyersLevel { get; set; }

    [ObservableProperty]
    public partial string FyersSummary { get; set; } = "Checking";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFyersValue))]
    public partial string FyersValue { get; set; } = Display.Missing;

    public bool HasFyersValue => FyersValue != Display.Missing;

    [ObservableProperty]
    public partial string? FyersDetail { get; set; }

    [ObservableProperty]
    public partial double FyersValidity { get; set; }

    [ObservableProperty]
    public partial StatusLevel NavLevel { get; set; }

    [ObservableProperty]
    public partial string NavSummary { get; set; } = "Checking";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNavValue))]
    public partial string NavValue { get; set; } = Display.Missing;

    public bool HasNavValue => NavValue != Display.Missing;

    [ObservableProperty]
    public partial string? NavDetail { get; set; }

    [ObservableProperty]
    public partial StatusLevel RequestsLevel { get; set; }

    [ObservableProperty]
    public partial string RequestsSummary { get; set; } = "Checking";

    [ObservableProperty]
    public partial string RequestsValue { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string? RequestsDetail { get; set; }

    /// <summary>The newest of today's requests that failed or has symbols that failed, which "See errors" opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblemRequest))]
    [NotifyCanExecuteChangedFor(nameof(ShowProblemsCommand))]
    public partial QuoteRequestSummary? ProblemRequest { get; set; }

    public bool HasProblemRequest => ProblemRequest is not null;

    /// <summary>Raised by "See errors", to open the request on the History page.</summary>
    public event EventHandler<QuoteRequestSummary>? ProblemsRequested;

    [ObservableProperty]
    public partial LiveFeedTile LiveFeed { get; set; } = LiveFeedTile.Unknown("Checking", "Shown once the API responds.");

    [ObservableProperty]
    public partial IReadOnlyList<TopHoldingRow> TopHoldings { get; set; } = [];

    /// <summary>Today's estimated move of all listed funds together; null without live prices.</summary>
    [ObservableProperty]
    public partial PortfolioMove? Portfolio { get; set; }

    /// <summary>Whether today's gain or loss in rupees is showing; it's hidden unless the user asks to see it.</summary>
    [ObservableProperty]
    public partial bool IsAmountShown { get; private set; }

    private CancellationTokenSource? _amountTimer;

    /// <summary>Shows the rupee amount for 5 seconds; pressed again while showing, hides it.</summary>
    [RelayCommand]
    private void ShowAmount()
    {
        _amountTimer?.Cancel();
        _amountTimer = null;
        if (IsAmountShown)
        {
            IsAmountShown = false;
            return;
        }

        IsAmountShown = true;
        _amountTimer = new CancellationTokenSource();
        _ = HideAmountAsync(_amountTimer.Token);
    }

    private async Task HideAmountAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            IsAmountShown = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Nifty 50, Sensex and Nifty Bank from the live feed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIndices))]
    public partial IReadOnlyList<MarketIndexTile> Indices { get; set; } = [];

    /// <summary>Whether any index has had a tick; the strip is hidden until then.</summary>
    public bool HasIndices => Indices.Any(i => i.Quote is not null);

    /// <summary>Today's advances and declines among every stock the funds hold; follows the live ticks.</summary>
    [ObservableProperty]
    public partial MarketBreadth? Breadth { get; set; }

    [ObservableProperty]
    public partial string TopHoldingsEmptyText { get; set; } = "Loading…";


    [ObservableProperty]
    public partial StatusLevel AlertLevel { get; set; }

    [ObservableProperty]
    public partial string? AlertTitle { get; set; }

    [ObservableProperty]
    public partial string? AlertText { get; set; }

    [ObservableProperty]
    public partial bool AlertOffersSettings { get; set; }

    [ObservableProperty]
    public partial bool AlertOffersLogin { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastChecked { get; set; }

    public void Start() => _ = RefreshIfIdleAsync();

    /// <summary>Refreshes, or refreshes once more after the one already running, so no change is missed.</summary>
    public async Task RefreshIfIdleAsync()
    {
        if (RefreshCommand.IsRunning)
        {
            _refreshAgain = true;
            return;
        }

        do
        {
            _refreshAgain = false;
            await RefreshCommand.ExecuteAsync(null);
        }
        while (_refreshAgain);
    }

    private async Task OnHubStateChangedAsync()
    {
        HubConnectionState previous = _hubState;
        _hubState = _connection.HubState;
        if (_online)
            ShowHub();
        // A dropped hub usually means the API went away; check, since nothing is polled.
        if (previous == HubConnectionState.Connected && _hubState != HubConnectionState.Connected)
            await RefreshIfIdleAsync();
    }

    private void ShowHub()
    {
        (ApiLevel, ApiValue) = _hubState switch
        {
            HubConnectionState.Connected => (StatusLevel.Good, "Connected"),
            HubConnectionState.Reconnecting => (StatusLevel.Warning, "Reconnecting"),
            _ => (StatusLevel.Warning, "Connecting"),
        };
    }

    private void ShowLiveFeed(LiveFeedStatus status)
    {
        _liveStatus = status;
        LiveFeed = LiveFeedTile.From(status);
    }

    private void ShowLiveQuotes(LiveQuoteList batch)
    {
        if (_liveStatus is { } status && (status.State != batch.State || status.LastTickAt != batch.LastTickAt))
            ShowLiveFeed(status with { State = batch.State, LastTickAt = batch.LastTickAt });

        Dictionary<string, LiveQuote> quotes = batch.Quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
        ShowIndices(batch.Quotes);
        bool moved = false;
        foreach (LiveQuote quote in quotes.Values)
        {
            if (quote.ChangePercent is not null && _dayChanges.TryGetValue(quote.Symbol, out decimal? change) && change != quote.ChangePercent)
            {
                _dayChanges[quote.Symbol] = quote.ChangePercent;
                moved = true;
            }
        }
        if (moved)
            Breadth = MarketBreadth.From(_dayChanges.Values);

        if (_top is null || !TopHoldings.Any(r => quotes.ContainsKey(r.Symbol)))
            return;

        TopHoldings = [.. TopHoldings.Select(r => quotes.TryGetValue(r.Symbol, out LiveQuote? q) ? r.WithPrice(q) : r)];
        LastChecked = DateTimeOffset.Now;
    }

    private void ShowIndices(IEnumerable<LiveQuote> quotes)
    {
        bool changed = false;
        foreach (LiveQuote quote in quotes.Where(q => MarketIndex.IsIndex(q.Symbol)))
        {
            _indexQuotes[quote.Symbol] = quote;
            changed = true;
        }
        if (changed || Indices.Count == 0)
            Indices = MarketIndexTile.From(_indexQuotes);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        QuoteSnapClient client = _connection.Client;
        Uri baseAddress = _connection.Settings.BaseAddress;
        try
        {
            ServiceStatus status = await client.GetStatusAsync();
            _online = true;
            _hubState = _connection.HubState;
            ApiSummary = "Online";
            ShowHub();
            ApiDetail = $"v{Display.Version(status.Version)} · {baseAddress.Authority}";
            ApiDetailTooltip = $"State of the live connection the API pushes updates over. Server time {Display.Time(status.ServerTime)}.";
            ClearAlert();
            ShowFyers(status.Fyers, status.ServerTime);
        }
        catch (Exception ex)
        {
            ShowOffline(ex, baseAddress);
            LastChecked = DateTimeOffset.Now;
            return;
        }

        try
        {
            ShowNav(await client.GetNavStatusAsync());
        }
        catch (Exception ex)
        {
            NavLevel = StatusLevel.Warning;
            NavSummary = "Unavailable";
            NavValue = Display.Missing;
            NavDetail = ErrorText.Describe(ex, baseAddress);
        }

        try
        {
            await _exclusions.RefreshAsync();
            ShowRequests(await client.ListAsync(limit: TodayPageSize));
        }
        catch (Exception ex)
        {
            RequestsLevel = StatusLevel.Warning;
            RequestsSummary = "Unavailable";
            RequestsDetail = ErrorText.Describe(ex, baseAddress);
            ProblemRequest = null;
        }

        try
        {
            ShowLiveFeed(await client.GetLiveFeedStatusAsync());
        }
        catch (Exception ex)
        {
            LiveFeed = LiveFeedTile.Unknown("Unavailable", ErrorText.Describe(ex, baseAddress));
        }

        try
        {
            ShowIndices((await client.GetLiveQuotesAsync()).Quotes);
        }
        catch (Exception)
        {
            // The strip keeps its last ticks; the live feed tile shows what went wrong.
        }

        try
        {
            TopHoldingList all = await client.GetTopHoldingsAsync(null);
            ShowTopHoldings(all with { Holdings = [.. all.Holdings.Take(TopCount)] });
            _dayChanges = all.Holdings.ToDictionary(h => h.Symbol, h => h.ChangePercent, StringComparer.OrdinalIgnoreCase);
            Breadth = MarketBreadth.From(_dayChanges.Values);
        }
        catch (Exception ex)
        {
            _top = null;
            _dayChanges.Clear();
            Breadth = null;
            TopHoldings = [];
            TopHoldingsEmptyText = ErrorText.Describe(ex, baseAddress);
        }

        try
        {
            Portfolio = PortfolioMove.From(await client.GetNavEstimatesAsync());
        }
        catch (Exception)
        {
            Portfolio = null;
        }

        LastChecked = DateTimeOffset.Now;
    }

    private void ShowOffline(Exception ex, Uri baseAddress)
    {
        bool keyRejected = ex is QuoteSnapApiException { StatusCode: HttpStatusCode.Unauthorized };
        _online = false;
        _liveStatus = null;
        _top = null;
        ApiLevel = StatusLevel.Bad;
        ApiSummary = keyRejected ? "Key rejected" : "Offline";
        ApiValue = keyRejected ? "401" : "Offline";
        ApiDetail = keyRejected ? "Check the API key in Settings" : $"No response from {baseAddress.Authority}";
        ApiDetailTooltip = ErrorText.Describe(ex, baseAddress);

        FyersLevel = NavLevel = RequestsLevel = StatusLevel.Unknown;
        FyersSummary = NavSummary = RequestsSummary = "Unknown";
        FyersValue = NavValue = RequestsValue = Display.Missing;
        FyersDetail = NavDetail = RequestsDetail = "Shown once the API responds.";
        ProblemRequest = null;
        FyersValidity = 0;
        LiveFeed = LiveFeedTile.Unknown("Unknown", "Shown once the API responds.");
        TopHoldings = [];
        Portfolio = null;
        _indexQuotes.Clear();
        Indices = [];
        _dayChanges.Clear();
        Breadth = null;
        TopHoldingsEmptyText = "Top holdings appear here once the API responds.";

        AlertLevel = StatusLevel.Bad;
        AlertOffersSettings = true;
        AlertOffersLogin = false;
        if (keyRejected)
        {
            AlertTitle = "The API rejected the key";
            AlertText = "Check the API key in Settings; it must match the key the QuoteSnap API is configured with.";
        }
        else
        {
            AlertTitle = "QuoteSnap API is offline";
            AlertText = $"Can't reach {baseAddress.Authority}. Make sure the QuoteSnap API is running, or check its address in Settings.";
        }
    }

    private void ClearAlert()
    {
        AlertTitle = null;
        AlertText = null;
        AlertOffersSettings = false;
        AlertOffersLogin = false;
    }

    private void ShowFyers(FyersSessionInfo fyers, DateTimeOffset now)
    {
        TimeSpan? left = fyers.RefreshTokenExpiresAt - now;
        FyersValidity = left is { } l ? Math.Clamp(l / LoginLifetime, 0, 1) : 0;
        FyersValue = left is { TotalDays: >= 1 } days ? Display.Count((int)days.TotalDays, "day")
            : left is { TotalHours: > 0 } hours ? Display.Count((int)Math.Ceiling(hours.TotalHours), "hour")
            : Display.Missing;
        string account = fyers.FyersId ?? "No account";
        switch (fyers.State)
        {
            case FyersAuthState.Ok or FyersAuthState.AccessTokenExpired:
                bool expiringSoon = left < LoginExpiryWarning;
                FyersLevel = expiringSoon ? StatusLevel.Warning : StatusLevel.Good;
                FyersSummary = expiringSoon ? "Expires soon" : "Logged in";
                FyersDetail = fyers.State == FyersAuthState.Ok
                    ? $"{account} · token renews {Display.WhenInSentence(fyers.AccessTokenExpiresAt)}"
                    : $"{account} · token renews on next request";
                if (expiringSoon)
                    ShowLoginAlert(StatusLevel.Warning, "Fyers login expires soon", $"The login for {account} ends {Display.WhenInSentence(fyers.RefreshTokenExpiresAt)}.");
                break;
            default:
                FyersLevel = StatusLevel.Bad;
                FyersSummary = "Login needed";
                FyersValue = "Expired";
                FyersValidity = 0;
                FyersDetail = fyers.FyersId is null ? "No Fyers login stored" : $"{account} · login has expired";
                ShowLoginAlert(StatusLevel.Bad, "Fyers login needed", "Quote requests are refused until you log in again.");
                break;
        }
    }

    private void ShowLoginAlert(StatusLevel level, string title, string reason)
    {
        AlertLevel = level;
        AlertTitle = title;
        AlertText = $"{reason} Log in to Fyers again on the Fyers login page.";
        AlertOffersSettings = false;
        AlertOffersLogin = true;
    }

    private void ShowNav(NavStatus nav)
    {
        if (nav.LoadedAt is not { } loadedAt)
        {
            NavLevel = StatusLevel.Warning;
            NavSummary = "Not loaded";
            NavValue = "0";
            NavDetail = "Refresh NAVs on the Mutual funds page";
            return;
        }

        bool stale = loadedAt.ToLocalTime().Date < DateTime.Today;
        NavLevel = stale ? StatusLevel.Warning : StatusLevel.Good;
        NavSummary = stale ? "Stale" : "Fresh";
        NavValue = nav.SchemeCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        NavDetail = $"Loaded {Display.WhenInSentence(loadedAt)} · NAV date {Display.Date(nav.LatestNavDate)}";
    }

    private void ShowTopHoldings(TopHoldingList list)
    {
        _top = list;
        decimal largest = list.Holdings.Count > 0 ? list.Holdings.Max(h => h.Percent) : 0;
        TopHoldings = [.. list.Holdings.Select((h, i) => new TopHoldingRow(i + 1, h, largest, list.FundCount))];
        TopHoldingsEmptyText = list.FundCount == 0 ? "No holdings yet. Add funds on the Mutual funds page and fetch their ScanX holdings on the Holdings page." : "None of your funds' holdings are listed stocks.";
    }

    /// <summary>"Average weight across 3 funds · prices from today 10:42" ("Weighted by value across…" once every fund has units), or a hint to get live prices.</summary>
    public static string? TopHoldingsText(TopHoldingList list) => list.FundCount == 0 ? null : $"{(list.ByValue ? "Weighted by value" : "Average weight")} across {Display.Count(list.FundCount, "fund")} · " + (list.PricesAt is { } at ? $"prices from {Display.WhenInSentence(at)}" : "get live prices on the Holdings page");

    private void ShowRequests(QuoteRequestPage page)
    {
        _requests = page;
        List<QuoteRequestSummary> today = [.. page.Items.Where(s => s.CreatedAt.ToLocalTime().Date == DateTime.Today)];
        bool more = page.NextBefore is not null && today.Count == page.Items.Count;
        int running = today.Count(s => !s.Status.IsTerminal());
        int failed = today.Count(s => s.Status == QuoteRequestStatus.Failed);
        // Excluding the failed symbols deals with the errors of the requests made before then.
        DateTimeOffset dealtWith = _exclusions.ExcludedAt ?? DateTimeOffset.MinValue;
        List<QuoteRequestSummary> withErrors = [.. today.Where(s => s.Status == QuoteRequestStatus.CompletedWithErrors && s.CreatedAt > dealtWith)];
        int symbols = today.Sum(s => s.TotalSymbols);
        int ok = today.Sum(s => s.SucceededCount);

        RequestsValue = today.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + (more ? "+" : "");
        (RequestsLevel, RequestsSummary) =
            running > 0 ? (StatusLevel.Busy, $"{running} running")
            : failed > 0 ? (StatusLevel.Bad, $"{failed} failed")
            : withErrors.Count > 0 ? (StatusLevel.Warning, $"{withErrors.Count} with errors")
            : today.Count > 0 ? (StatusLevel.Good, "All OK")
            : (StatusLevel.Unknown, "None yet");
        RequestsDetail = today.Count == 0
            ? "Nothing fetched today"
            : $"{ok:N0} of {Display.Count(symbols, "symbol")} fetched OK" + (_exclusions.Today.Count > 0 ? $" · {_exclusions.Today.Count} excluded" : "");
        ProblemRequest = today.FirstOrDefault(s => s.Status == QuoteRequestStatus.Failed || withErrors.Contains(s));
    }

    /// <summary>Opens the newest of today's requests with errors on the History page.</summary>
    [RelayCommand(CanExecute = nameof(HasProblemRequest))]
    private void ShowProblems()
    {
        if (ProblemRequest is { } request)
            ProblemsRequested?.Invoke(this, request);
    }
}

using System.Diagnostics;
using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private const int RecentCount = 8;
    private const int TodayPageSize = 50;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LoginExpiryWarning = TimeSpan.FromDays(2);
    private static readonly TimeSpan LoginLifetime = TimeSpan.FromDays(15);

    private readonly QuoteSnapConnection _connection;
    private readonly DispatcherTimer _timer;

    public DashboardViewModel(QuoteSnapConnection connection)
    {
        _connection = connection;
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += async (_, _) => await RefreshIfIdleAsync();
        connection.Changed += async (_, _) =>
        {
            OnPropertyChanged(nameof(ConnectionHost));
            await RefreshIfIdleAsync();
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

    [ObservableProperty]
    public partial IReadOnlyList<RequestRow> RecentRequests { get; set; } = [];

    [ObservableProperty]
    public partial string RecentEmptyText { get; set; } = "Loading…";

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

    public void Start()
    {
        _timer.Start();
        _ = RefreshIfIdleAsync();
    }

    public async Task RefreshIfIdleAsync()
    {
        if (!RefreshCommand.IsRunning)
            await RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        QuoteSnapClient client = _connection.Client;
        Uri baseAddress = _connection.Settings.BaseAddress;
        try
        {
            long start = Stopwatch.GetTimestamp();
            ServiceStatus status = await client.GetStatusAsync();
            double latencyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            ApiLevel = StatusLevel.Good;
            ApiSummary = "Online";
            ApiValue = $"{latencyMs:0} ms";
            ApiDetail = $"v{Display.Version(status.Version)} · {baseAddress.Authority}";
            ApiDetailTooltip = $"Response time of the last status check. Server time {Display.Time(status.ServerTime)}.";
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
            ShowRequests(await client.ListAsync(limit: TodayPageSize));
        }
        catch (Exception ex)
        {
            RequestsLevel = StatusLevel.Warning;
            RequestsSummary = "Unavailable";
            RecentEmptyText = ErrorText.Describe(ex, baseAddress);
        }

        LastChecked = DateTimeOffset.Now;
    }

    private void ShowOffline(Exception ex, Uri baseAddress)
    {
        bool keyRejected = ex is QuoteSnapApiException { StatusCode: HttpStatusCode.Unauthorized };
        ApiLevel = StatusLevel.Bad;
        ApiSummary = keyRejected ? "Key rejected" : "Offline";
        ApiValue = keyRejected ? "401" : "Offline";
        ApiDetail = keyRejected ? "Check the API key in Settings" : $"No response from {baseAddress.Authority}";
        ApiDetailTooltip = ErrorText.Describe(ex, baseAddress);

        FyersLevel = NavLevel = RequestsLevel = StatusLevel.Unknown;
        FyersSummary = NavSummary = RequestsSummary = "Unknown";
        FyersValue = NavValue = RequestsValue = Display.Missing;
        FyersDetail = NavDetail = RequestsDetail = "Shown once the API responds.";
        FyersValidity = 0;
        RecentRequests = [];
        RecentEmptyText = "Recent requests appear here once the API responds.";

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

    private void ShowRequests(QuoteRequestPage page)
    {
        RecentRequests = [.. page.Items.Take(RecentCount).Select(s => new RequestRow(s))];
        RecentEmptyText = "No quote requests yet. Fetch quotes to see them here.";

        List<QuoteRequestSummary> today = [.. page.Items.Where(s => s.CreatedAt.ToLocalTime().Date == DateTime.Today)];
        bool more = page.NextBefore is not null && today.Count == page.Items.Count;
        int running = today.Count(s => !s.Status.IsTerminal());
        int failed = today.Count(s => s.Status == QuoteRequestStatus.Failed);
        int withErrors = today.Count(s => s.Status == QuoteRequestStatus.CompletedWithErrors);
        int symbols = today.Sum(s => s.TotalSymbols);
        int ok = today.Sum(s => s.SucceededCount);

        RequestsValue = today.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + (more ? "+" : "");
        (RequestsLevel, RequestsSummary) =
            running > 0 ? (StatusLevel.Busy, $"{running} running")
            : failed > 0 ? (StatusLevel.Bad, $"{failed} failed")
            : withErrors > 0 ? (StatusLevel.Warning, $"{withErrors} with errors")
            : today.Count > 0 ? (StatusLevel.Good, "All OK")
            : (StatusLevel.Unknown, "None yet");
        RequestsDetail = today.Count == 0
            ? "Nothing fetched today"
            : $"{ok:N0} of {Display.Count(symbols, "symbol")} fetched OK";
    }
}

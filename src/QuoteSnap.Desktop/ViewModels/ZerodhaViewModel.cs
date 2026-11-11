using System.Net;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Settings;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>One line of the last sync's report.</summary>
public sealed record ZerodhaSyncRow(string Change, string Name, string Detail);

/// <summary>
/// The Zerodha page: syncs My funds with the Zerodha mutual fund holdings. Every sync logs in to Kite in the browser,
/// and the service drops the token as soon as it has read the holdings, so no Zerodha login is kept anywhere.
/// </summary>
public sealed partial class ZerodhaViewModel : ObservableObject
{
    private readonly QuoteSnapConnection _connection;
    private CancellationTokenSource? _sync;

    public ZerodhaViewModel(QuoteSnapConnection connection, SettingsStore store)
    {
        _connection = connection;
        LoadedZerodhaApp loaded = store.LoadZerodhaApp();
        ApiKey = loaded.Details.ApiKey ?? "";
        ApiSecret = loaded.Details.ApiSecret ?? "";
        RedirectUri = loaded.Details.RedirectUri;
        DetailsSource = loaded.Source;
        connection.Changed += (_, _) => OnPropertyChanged(nameof(HasAdminKey));
    }

    /// <summary>Raised after a sync changed the fund list, so other pages can refresh.</summary>
    public event EventHandler? FundsSynced;

    /// <summary>Raised when a sync that sent you to the browser ends, however it ended, so the app can come back to the front.</summary>
    public event EventHandler? BrowserLoginEnded;

    /// <summary>Raised to open the Settings page, where the admin key is set.</summary>
    public event EventHandler? SettingsRequested;

    [ObservableProperty]
    public partial string ApiKey { get; set; } = "";

    [ObservableProperty]
    public partial string ApiSecret { get; set; } = "";

    [ObservableProperty]
    public partial string RedirectUri { get; set; } = ZerodhaLoginOptions.DefaultRedirectUri;

    [ObservableProperty]
    public partial string DetailsSource { get; set; } = "";

    public bool HasAdminKey => _connection.Settings.AdminApiKey is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDetails))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand), nameof(CancelCommand))]
    public partial bool IsSyncing { get; set; }

    public bool CanEditDetails => !IsSyncing;

    [ObservableProperty]
    public partial string? Step { get; set; }

    /// <summary>The Kite login page of the current sync, to open again or copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginUrl))]
    public partial string? LoginUrl { get; set; }

    public bool HasLoginUrl => LoginUrl is not null;

    [ObservableProperty]
    public partial string? MessageTitle { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    /// <summary>What the last sync added, updated, removed or couldn't match; unchanged funds are left out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    public partial IReadOnlyList<ZerodhaSyncRow> Changes { get; set; } = [];

    public bool HasChanges => Changes.Count > 0;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private bool CanSync() => !IsSyncing;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        Message = null;
        MessageTitle = null;
        if (ReadCredentials() is not { } app)
            return;

        using CancellationTokenSource cancellation = new();
        _sync = cancellation;
        IsSyncing = true;
        Step = "Opening the Zerodha login page…";
        ZerodhaLoginOptions options = new()
        {
            RedirectUri = RedirectUri.Trim(),
            OpenBrowser = url =>
            {
                LoginUrl = url;
                FyersLoginOptions.DefaultOpenBrowser(url);
                Step = "Log in to Zerodha in your browser. The sync continues by itself once Zerodha sends you back (within 5 minutes).";
            },
        };
        try
        {
            ZerodhaMfSyncResult result = await new ZerodhaMfSyncFlow(_connection.Client).SyncWithBrowserLoginAsync(app, options, cancellation.Token);
            ShowResult(result);
            if (result.Added.Count + result.Updated.Count + result.Removed.Count > 0)
                FundsSynced?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Report(StatusLevel.Warning, "Sync cancelled. Nothing was changed.");
        }
        catch (ZerodhaLoginException ex)
        {
            Report(StatusLevel.Bad, ex.Message);
        }
        catch (QuoteSnapApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Report(StatusLevel.Bad, "QuoteSnap rejected the admin key. It must match Auth__AdminApiKey in the API's .env; check it in Settings.");
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"The sync failed, so My funds is unchanged. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
        finally
        {
            bool openedBrowser = LoginUrl is not null;
            _sync = null;
            LoginUrl = null;
            Step = null;
            IsSyncing = false;
            if (openedBrowser)
                BrowserLoginEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanCancel() => IsSyncing;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _sync?.Cancel();

    [RelayCommand]
    private void OpenLoginPage()
    {
        if (LoginUrl is { } url)
            FyersLoginOptions.DefaultOpenBrowser(url);
    }

    [RelayCommand]
    private void CopyLoginUrl()
    {
        if (LoginUrl is { } url)
            Clipboard.SetText(url);
    }

    private ZerodhaAppCredentials? ReadCredentials()
    {
        if (!HasAdminKey)
        {
            Report(StatusLevel.Bad, "Syncing needs the admin API key. Set Auth__AdminApiKey in the repository's .env, or enter it in Settings.");
            return null;
        }

        string apiKey = ApiKey.Trim();
        string apiSecret = ApiSecret.Trim();
        if (apiKey.Length == 0 || apiSecret.Length == 0)
        {
            Report(StatusLevel.Bad, $"Enter the Kite Connect {(apiKey.Length == 0 ? apiSecret.Length == 0 ? "API key and API secret" : "API key" : "API secret")}.");
            return null;
        }

        if (!Uri.TryCreate(RedirectUri.Trim(), UriKind.Absolute, out _))
        {
            Report(StatusLevel.Bad, $"The redirect URL must be the full address registered on the Kite Connect app, e.g. {ZerodhaLoginOptions.DefaultRedirectUri}.");
            return null;
        }

        return new ZerodhaAppCredentials(apiKey, apiSecret);
    }

    private void ShowResult(ZerodhaMfSyncResult result)
    {
        List<ZerodhaSyncRow> rows = [
            .. result.Added.Select(f => new ZerodhaSyncRow("Added", f.SchemeName, Units(f.Units))),
            .. result.Updated.Select(f => new ZerodhaSyncRow("Updated", f.SchemeName, Units(f.Units))),
            .. result.Removed.Select(f => new ZerodhaSyncRow("Removed", f.SchemeName, "Not held in Zerodha")),
            .. result.Unmatched.Select(h => new ZerodhaSyncRow("Not matched", h.Fund, $"{h.Isin} · {Units(h.Units)} · no Direct Plan scheme in AMFI's NAV file")),
        ];
        Changes = rows;

        string counts = $"{result.Added.Count} added, {result.Updated.Count} updated, {result.Removed.Count} removed, {result.Unchanged.Count} unchanged";
        if (result.Unmatched.Count > 0)
            Report(StatusLevel.Warning, $"{counts}. {Display.Count(result.Unmatched.Count, "holding")} couldn't be matched to a fund and were left out.", $"Synced with {result.UserId}");
        else
            Report(StatusLevel.Good, $"{counts}.", $"Synced with {result.UserId}");
    }

    private static string Units(decimal? units) => units is { } u ? $"{u:#,##0.####} units" : Display.Missing;

    private void Report(StatusLevel level, string message, string? title = null)
    {
        MessageLevel = level;
        MessageTitle = title;
        Message = message;
    }
}

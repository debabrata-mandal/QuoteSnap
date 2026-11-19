using System.Net.Http;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Settings;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly QuoteSnapConnection _connection;
    private readonly AutoPriceFetcher _autoPrices;
    private readonly bool _loaded;

    public SettingsViewModel(SettingsStore store, QuoteSnapConnection connection, LoadedSettings loaded, AutoPriceFetcher autoPrices)
    {
        _store = store;
        _connection = connection;
        _autoPrices = autoPrices;
        Theme = ThemeManager.Current;
        AutoFetchPrices = store.LoadAutoFetchPrices();
        _loaded = true;
        autoPrices.Checked += text => AutoFetchText = text;
        Show(loaded);
    }

    /// <summary>Whether to request the holdings' live prices every 5 minutes while NSE is trading; saved straight away.</summary>
    [ObservableProperty]
    public partial bool AutoFetchPrices { get; set; }

    /// <summary>The last automatic check's result and live countdown; null while disabled.</summary>
    [ObservableProperty]
    public partial string? AutoFetchText { get; set; }

    /// <summary>Whether the API posts the saved NAV estimates to Slack while NSE is trading; saved on the API straight away.</summary>
    [ObservableProperty]
    public partial bool SlackEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsSlackConfigured { get; set; }

    [ObservableProperty]
    public partial string SlackText { get; set; } = SlackIdleText;

    private const string SlackIdleText = "The API posts each fund's saved estimate to your Slack channel while NSE is trading.";

    private bool _showingSlack;
    private SlackStatus? _slack;
    private CancellationTokenSource? _slackCountdown;

    // A Send test result, shown in place of the API's last report until the API reports a newer one.
    private (DateTimeOffset At, string Text)? _slackNote;

    /// <summary>The colour theme; a change applies at once and is saved straight away.</summary>
    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    [ObservableProperty]
    public partial string BaseAddressText { get; set; } = "";

    [ObservableProperty]
    public partial string ApiKey { get; set; } = "";

    [ObservableProperty]
    public partial bool IsApiKeyVisible { get; set; }

    [ObservableProperty]
    public partial string AdminApiKey { get; set; } = "";

    [ObservableProperty]
    public partial bool IsAdminApiKeyVisible { get; set; }

    [ObservableProperty]
    public partial string Source { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    [ObservableProperty]
    public partial string? MessagePill { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    public string SettingsPath => _store.SettingsPath;

    public string AppVersion { get; } = Display.Version(
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0");

    public string Runtime { get; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    partial void OnThemeChanged(AppTheme value)
    {
        if (value == ThemeManager.Current)
            return;

        ThemeManager.Apply(value);
        try
        {
            _store.SaveTheme(value);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Report(StatusLevel.Warning, "Theme not saved", $"The {value} theme applies until you close the app, but it couldn't be saved: {ex.Message}");
        }
    }

    partial void OnAutoFetchPricesChanged(bool value)
    {
        if (!_loaded)
            return;

        _autoPrices.SetEnabled(value);
        if (!value)
            AutoFetchText = null;
        try
        {
            _store.SaveAutoFetchPrices(value);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AutoFetchText = $"This applies until you close the app, but it couldn't be saved: {ex.Message}";
        }
    }

    /// <summary>Reads whether the Slack reports are on, each time the Settings tab opens.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            ShowSlack(await _connection.Client.GetSlackAsync());
        }
        catch (Exception ex)
        {
            StopSlackCountdown();
            IsSlackConfigured = false;
            SlackText = $"Couldn't read the Slack setting: {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}";
        }
    }

    partial void OnSlackEnabledChanged(bool value)
    {
        if (!_showingSlack)
            _ = SetSlackAsync(value);
    }

    private async Task SetSlackAsync(bool enabled)
    {
        try
        {
            ShowSlack(await _connection.Client.SetSlackAsync(enabled));
        }
        catch (Exception ex)
        {
            StopSlackCountdown();
            _showingSlack = true;
            SlackEnabled = !enabled;
            _showingSlack = false;
            SlackText = $"Not saved: {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}";
        }
    }

    [RelayCommand]
    private async Task TestSlackAsync()
    {
        StopSlackCountdown();
        SlackText = "Posting to Slack…";
        try
        {
            await _connection.Client.SendSlackTestAsync();
            _slackNote = (DateTimeOffset.Now, $"Test posted at {DateTimeOffset.Now:HH:mm}; check your Slack channel.");
        }
        catch (Exception ex)
        {
            _slackNote = (DateTimeOffset.Now, $"Test failed: {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }

        if (_slack is { } status)
            ShowSlack(status);
        else
            SlackText = _slackNote.Value.Text;
    }

    private void ShowSlack(SlackStatus status)
    {
        _slack = status;
        _showingSlack = true;
        IsSlackConfigured = status.Configured;
        SlackEnabled = status.Enabled;
        _showingSlack = false;
        if (_slackNote is { } note && status.LastCheckedAt > note.At)
            _slackNote = null;

        if (status is { Enabled: true, NextReportAt: not null })
        {
            ShowSlackCountdown();
            if (_slackCountdown is null)
            {
                _slackCountdown = new CancellationTokenSource();
                _ = RunSlackCountdownAsync(_slackCountdown.Token);
            }

            return;
        }

        StopSlackCountdown();
        SlackText = _slackNote?.Text
            ?? (!status.Configured ? "The API has no Slack bot yet. Set Slack__BotToken and Slack__ChannelId in its .env and restart it (README: Slack reports)."
            : status.Enabled ? $"On: posts every {status.IntervalMinutes} minutes while NSE is trading, once an estimate is saved today."
            : SlackIdleText);
    }

    /// <summary>Updates the countdown every second, and reads the status again once the API's post is due.</summary>
    private async Task RunSlackCountdownAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset lastRead = DateTimeOffset.Now;
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                DateTimeOffset now = DateTimeOffset.Now;
                if (_slack?.NextReportAt is { } next && now > next.AddSeconds(5) && now - lastRead > TimeSpan.FromSeconds(10))
                {
                    lastRead = now;
                    try
                    {
                        SlackStatus status = await _connection.Client.GetSlackAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        ShowSlack(status);
                        continue;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        SlackText = $"Couldn't read the Slack status: {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}";
                        continue;
                    }
                }

                ShowSlackCountdown();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ShowSlackCountdown()
    {
        if (_slack is not { NextReportAt: { } next } status)
            return;

        TimeSpan remaining = next > DateTimeOffset.Now ? next - DateTimeOffset.Now : TimeSpan.Zero;
        string result = _slackNote?.Text ?? status.LastResult ?? $"On: posts every {status.IntervalMinutes} minutes while NSE is trading.";
        SlackText = $"{result} Next post in {remaining:mm\\:ss} (at {next.ToLocalTime():HH:mm:ss}).";
    }

    private void StopSlackCountdown()
    {
        _slackCountdown?.Cancel();
        _slackCountdown?.Dispose();
        _slackCountdown = null;
    }

    [RelayCommand]
    private void Save()
    {
        if (Read() is not { } settings)
            return;

        _store.Save(settings);
        _connection.Apply(settings);
        Show(_store.Load());
        Report(StatusLevel.Good, "Saved", "The app is now using these settings.");
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (Read() is not { } settings)
            return;

        Report(StatusLevel.Busy, "Testing", $"Connecting to {settings.BaseAddress.Authority}…");
        (HttpClient http, QuoteSnapClient client) = QuoteSnapConnection.Create(settings);
        using (http)
        {
            try
            {
                ServiceStatus status = await client.GetStatusAsync();
                string fyers = status.Fyers.State is FyersAuthState.Ok or FyersAuthState.AccessTokenExpired
                    ? "Fyers is logged in"
                    : "Fyers needs a new login";
                if (settings.AdminApiKey is null)
                {
                    Report(StatusLevel.Good, "Connected", $"QuoteSnap {Display.Version(status.Version)}; {fyers}. No admin key, so the Fyers login page can't log in.");
                    return;
                }

                try
                {
                    await client.GetFyersSessionAsync();
                    Report(StatusLevel.Good, "Connected", $"QuoteSnap {Display.Version(status.Version)}; {fyers}. The admin key works.");
                }
                catch (QuoteSnapApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    Report(StatusLevel.Warning, "Admin key rejected", $"QuoteSnap {Display.Version(status.Version)} is reachable, but it rejected the admin key; it must match Auth__AdminApiKey.");
                }
            }
            catch (Exception ex)
            {
                Report(StatusLevel.Bad, "Failed", ErrorText.Describe(ex, settings.BaseAddress));
            }
        }
    }

    [RelayCommand]
    private void Reset()
    {
        _store.Reset();
        LoadedSettings loaded = _store.Load();
        _connection.Apply(loaded.Settings);
        Show(loaded);
        Report(StatusLevel.Good, "Reset", "Saved settings removed; using the defaults again.");
    }

    private DesktopSettings? Read()
    {
        if (!DesktopSettings.TryParseBaseAddress(BaseAddressText, out Uri address))
        {
            Report(StatusLevel.Bad, "Invalid URL", "Enter the API's address as an http:// or https:// URL, e.g. http://127.0.0.1:8080/.");
            return null;
        }

        return new DesktopSettings(
            address,
            string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
            string.IsNullOrWhiteSpace(AdminApiKey) ? null : AdminApiKey.Trim());
    }

    private void Show(LoadedSettings loaded)
    {
        BaseAddressText = loaded.Settings.BaseAddress.AbsoluteUri;
        ApiKey = loaded.Settings.ApiKey ?? "";
        AdminApiKey = loaded.Settings.AdminApiKey ?? "";
        Source = loaded.Source;
        IsSaved = loaded.IsSaved;
    }

    private void Report(StatusLevel level, string pill, string message)
    {
        MessageLevel = level;
        MessagePill = pill;
        Message = message;
    }
}

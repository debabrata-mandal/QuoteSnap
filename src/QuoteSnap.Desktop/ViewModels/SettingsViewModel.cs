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

    public SettingsViewModel(SettingsStore store, QuoteSnapConnection connection, LoadedSettings loaded)
    {
        _store = store;
        _connection = connection;
        Theme = ThemeManager.Current;
        Show(loaded);
    }

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

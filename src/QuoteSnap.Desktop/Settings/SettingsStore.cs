using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetEnv;
using QuoteSnap.Client;

namespace QuoteSnap.Desktop.Settings;

/// <summary>
/// Saved settings live in %LOCALAPPDATA%\QuoteSnap, with the API keys, Fyers Secret ID and PIN encrypted for the
/// current Windows user. Until something is saved, they come from the repository's .env, as the API's and the login
/// tool's do.
/// </summary>
public sealed class SettingsStore(string settingsPath, IReadOnlyList<string> envSearchStarts)
{
    private const string EnvFileName = ".env";
    private const string ApiKeyVariable = "Auth__ApiKey";
    private const string AdminApiKeyVariable = "Auth__AdminApiKey";
    private const string FyersLoginFileName = "fyers-login.json";
    private const string AppearanceFileName = "appearance.json";
    private const string AutoPricesFileName = "auto-prices.json";
    private static readonly byte[] Entropy = "QuoteSnap.Desktop.ApiKey.v1"u8.ToArray();
    private static readonly byte[] FyersEntropy = "QuoteSnap.Desktop.FyersLogin.v1"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SettingsStore CreateDefault() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuoteSnap", "desktop-settings.json"), [AppContext.BaseDirectory, Environment.CurrentDirectory]);

    public string SettingsPath { get; } = settingsPath;

    public string FyersLoginPath { get; } = Path.Combine(Path.GetDirectoryName(settingsPath) ?? "", FyersLoginFileName);

    public string AppearancePath { get; } = Path.Combine(Path.GetDirectoryName(settingsPath) ?? "", AppearanceFileName);

    public string AutoPricesPath { get; } = Path.Combine(Path.GetDirectoryName(settingsPath) ?? "", AutoPricesFileName);

    /// <summary>The saved theme, or <see cref="AppTheme.Dark"/> when none is saved or the file can't be read.</summary>
    public AppTheme LoadTheme()
    {
        try
        {
            if (File.Exists(AppearancePath) && JsonSerializer.Deserialize<StoredAppearance>(File.ReadAllText(AppearancePath), Json) is { } stored && Enum.TryParse(stored.Theme, ignoreCase: true, out AppTheme theme) && Enum.IsDefined(theme))
                return theme;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }

        return AppTheme.Dark;
    }

    public void SaveTheme(AppTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppearancePath)!);
        File.WriteAllText(AppearancePath, JsonSerializer.Serialize(new StoredAppearance(theme.ToString()), Json));
    }

    /// <summary>Whether to fetch the holdings' live prices every 15 minutes while the market is open; off when unsaved or unreadable.</summary>
    public bool LoadAutoFetchPrices()
    {
        try
        {
            return File.Exists(AutoPricesPath) && JsonSerializer.Deserialize<StoredAutoPrices>(File.ReadAllText(AutoPricesPath), Json) is { Enabled: true };
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }

    public void SaveAutoFetchPrices(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AutoPricesPath)!);
        File.WriteAllText(AutoPricesPath, JsonSerializer.Serialize(new StoredAutoPrices(enabled), Json));
    }

    public LoadedSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            try
            {
                StoredSettings? stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath), Json);
                if (stored is not null && DesktopSettings.TryParseBaseAddress(stored.BaseAddress, out Uri address))
                {
                    string? adminKey = Unprotect(stored.ProtectedAdminApiKey, Entropy) ?? ReadEnvFile().Values.GetValueOrDefault(AdminApiKeyVariable);
                    return new LoadedSettings(
                        new DesktopSettings(address, Unprotect(stored.ProtectedApiKey, Entropy), adminKey), $"Saved in {SettingsPath}", true);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or CryptographicException or FormatException)
            {
                return Defaults($"{SettingsPath} could not be read ({ex.Message}); using defaults.");
            }
        }

        return Defaults(null);
    }

    public void Save(DesktopSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        StoredSettings stored = new(settings.BaseAddress.AbsoluteUri, Protect(settings.ApiKey, Entropy), Protect(settings.AdminApiKey, Entropy));
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored, Json));
    }

    public void Reset()
    {
        if (File.Exists(SettingsPath))
            File.Delete(SettingsPath);
    }

    /// <summary>The remembered Fyers details, or else Fyers__AppId, Fyers__SecretId, Fyers__Pin and Fyers__RedirectUri from .env.</summary>
    public LoadedFyersLogin LoadFyersLogin()
    {
        string? problem = null;
        if (File.Exists(FyersLoginPath))
        {
            try
            {
                StoredFyersLogin? stored = JsonSerializer.Deserialize<StoredFyersLogin>(File.ReadAllText(FyersLoginPath), Json);
                if (stored is not null)
                {
                    FyersLoginDetails remembered = new(stored.AppId, Unprotect(stored.ProtectedSecretId, FyersEntropy), Unprotect(stored.ProtectedPin, FyersEntropy), stored.RedirectUri ?? FyersLoginOptions.DefaultRedirectUri);
                    return new LoadedFyersLogin(remembered, true, $"Remembered on this PC in {FyersLoginPath}");
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or CryptographicException or FormatException)
            {
                problem = $"{FyersLoginPath} could not be read ({ex.Message}).";
            }
        }

        (string? envPath, IReadOnlyDictionary<string, string> env) = ReadEnvFile();
        FyersLoginDetails details = new(
            env.GetValueOrDefault("Fyers__AppId"),
            env.GetValueOrDefault("Fyers__SecretId"),
            env.GetValueOrDefault("Fyers__Pin"),
            env.GetValueOrDefault("Fyers__RedirectUri") ?? FyersLoginOptions.DefaultRedirectUri);
        string source = problem
            ?? ((details.AppId ?? details.SecretId ?? details.Pin) is not null ? $"Prefilled from {envPath}" : "Not saved on this PC");
        return new LoadedFyersLogin(details, false, source);
    }

    public void SaveFyersLogin(FyersLoginDetails details)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FyersLoginPath)!);
        StoredFyersLogin stored = new(details.AppId, Protect(details.SecretId, FyersEntropy), Protect(details.Pin, FyersEntropy), details.RedirectUri);
        File.WriteAllText(FyersLoginPath, JsonSerializer.Serialize(stored, Json));
    }

    public void ForgetFyersLogin()
    {
        if (File.Exists(FyersLoginPath))
            File.Delete(FyersLoginPath);
    }

    /// <summary>Zerodha__ApiKey, Zerodha__ApiSecret and Zerodha__RedirectUri from .env; the app never saves them itself.</summary>
    public LoadedZerodhaApp LoadZerodhaApp()
    {
        (string? envPath, IReadOnlyDictionary<string, string> env) = ReadEnvFile();
        ZerodhaAppDetails details = new(env.GetValueOrDefault("Zerodha__ApiKey"), env.GetValueOrDefault("Zerodha__ApiSecret"), env.GetValueOrDefault("Zerodha__RedirectUri") ?? ZerodhaLoginOptions.DefaultRedirectUri);
        return new LoadedZerodhaApp(details, (details.ApiKey ?? details.ApiSecret) is not null ? $"Prefilled from {envPath}" : "Not saved on this PC; add Zerodha__ApiKey and Zerodha__ApiSecret to the repository's .env to prefill them");
    }

    private LoadedSettings Defaults(string? problem)
    {
        (string? envPath, IReadOnlyDictionary<string, string> env) = ReadEnvFile();
        string? apiKey = env.GetValueOrDefault(ApiKeyVariable);
        string source = apiKey is not null
            ? $"API key from {ApiKeyVariable} in {envPath}"
            : $"Defaults (no {ApiKeyVariable} found in a {EnvFileName} file)";
        return new LoadedSettings(
            new DesktopSettings(DesktopSettings.DefaultBaseAddress, apiKey, env.GetValueOrDefault(AdminApiKeyVariable)), problem ?? source, false);
    }

    /// <summary>The non-blank values of the nearest .env file above any of the search starts.</summary>
    private (string? Path, IReadOnlyDictionary<string, string> Values) ReadEnvFile()
    {
        if (FindEnvFile() is not { } path)
            return (null, new Dictionary<string, string>());

        Dictionary<string, string> values = [];
        foreach (KeyValuePair<string, string> pair in Env.NoEnvVars().Load(path))
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
                values[pair.Key] = pair.Value.Trim();
        }

        return (path, values);
    }

    private string? FindEnvFile()
    {
        foreach (string start in envSearchStarts)
        {
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, EnvFileName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? Protect(string? value, byte[] entropy) => string.IsNullOrEmpty(value)
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? value, byte[] entropy) => string.IsNullOrEmpty(value)
        ? null
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), entropy, DataProtectionScope.CurrentUser));

    private sealed record StoredSettings(string BaseAddress, string? ProtectedApiKey, string? ProtectedAdminApiKey = null);

    private sealed record StoredFyersLogin(string? AppId, string? ProtectedSecretId, string? ProtectedPin, string? RedirectUri);

    private sealed record StoredAppearance(string? Theme);

    private sealed record StoredAutoPrices(bool Enabled);
}

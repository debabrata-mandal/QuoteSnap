using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNetEnv;

namespace QuoteSnap.Desktop.Settings;

/// <summary>
/// Saved settings live in %LOCALAPPDATA%\QuoteSnap, with the API key encrypted for the current Windows user.
/// Until something is saved, the API key comes from the repository's .env (Auth__ApiKey), as the API's does.
/// </summary>
public sealed class SettingsStore(string settingsPath, IReadOnlyList<string> envSearchStarts)
{
    private const string EnvFileName = ".env";
    private const string ApiKeyVariable = "Auth__ApiKey";
    private static readonly byte[] Entropy = "QuoteSnap.Desktop.ApiKey.v1"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static SettingsStore CreateDefault() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuoteSnap", "desktop-settings.json"),
        [AppContext.BaseDirectory, Environment.CurrentDirectory]);

    public string SettingsPath { get; } = settingsPath;

    public LoadedSettings Load()
    {
        if (File.Exists(SettingsPath))
        {
            try
            {
                StoredSettings? stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath), Json);
                if (stored is not null && DesktopSettings.TryParseBaseAddress(stored.BaseAddress, out Uri address))
                    return new LoadedSettings(new DesktopSettings(address, Unprotect(stored.ProtectedApiKey)), $"Saved in {SettingsPath}", true);
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
        StoredSettings stored = new(settings.BaseAddress.AbsoluteUri, Protect(settings.ApiKey));
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored, Json));
    }

    public void Reset()
    {
        if (File.Exists(SettingsPath))
            File.Delete(SettingsPath);
    }

    private LoadedSettings Defaults(string? problem)
    {
        string? envPath = FindEnvFile();
        string? apiKey = envPath is null ? null : ReadEnvValue(envPath, ApiKeyVariable);
        string source = apiKey is not null
            ? $"API key from {ApiKeyVariable} in {envPath}"
            : $"Defaults (no {ApiKeyVariable} found in a {EnvFileName} file)";
        return new LoadedSettings(new DesktopSettings(DesktopSettings.DefaultBaseAddress, apiKey), problem ?? source, false);
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

    private static string? ReadEnvValue(string path, string name)
    {
        foreach (KeyValuePair<string, string> pair in Env.NoEnvVars().Load(path))
        {
            if (pair.Key == name && !string.IsNullOrWhiteSpace(pair.Value))
                return pair.Value.Trim();
        }

        return null;
    }

    private static string? Protect(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? value) => string.IsNullOrEmpty(value)
        ? null
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));

    private sealed record StoredSettings(string BaseAddress, string? ProtectedApiKey);
}

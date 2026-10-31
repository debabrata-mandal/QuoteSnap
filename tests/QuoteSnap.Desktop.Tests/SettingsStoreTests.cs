using System.IO;
using QuoteSnap.Desktop.Settings;

namespace QuoteSnap.Desktop.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quotesnap-desktop-tests").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Without_saved_settings_the_api_key_comes_from_the_nearest_env_file()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "QUOTESNAP_DB_PASSWORD=x\nAuth__ApiKey=from-env-file-0000\n");
        string start = Directory.CreateDirectory(Path.Combine(_root, "src", "QuoteSnap.Desktop", "bin")).FullName;
        SettingsStore store = new(Path.Combine(_root, "settings", "desktop-settings.json"), [start]);

        LoadedSettings loaded = store.Load();

        Assert.False(loaded.IsSaved);
        Assert.Equal(DesktopSettings.DefaultBaseAddress, loaded.Settings.BaseAddress);
        Assert.Equal("from-env-file-0000", loaded.Settings.ApiKey);
        Assert.Contains(".env", loaded.Source);
    }

    [Fact]
    public void Without_an_env_file_there_is_no_api_key()
    {
        SettingsStore store = new(Path.Combine(_root, "desktop-settings.json"), [_root]);

        Assert.Null(store.Load().Settings.ApiKey);
    }

    [Fact]
    public void Saved_settings_round_trip_with_the_api_key_encrypted()
    {
        string path = Path.Combine(_root, "settings", "desktop-settings.json");
        SettingsStore store = new(path, [_root]);

        store.Save(new DesktopSettings(new Uri("http://192.168.1.20:8080/"), "saved-api-key-0000"));
        LoadedSettings loaded = store.Load();

        Assert.True(loaded.IsSaved);
        Assert.Equal(new Uri("http://192.168.1.20:8080/"), loaded.Settings.BaseAddress);
        Assert.Equal("saved-api-key-0000", loaded.Settings.ApiKey);
        Assert.DoesNotContain("saved-api-key-0000", File.ReadAllText(path));
    }

    [Fact]
    public void Reset_goes_back_to_the_defaults()
    {
        SettingsStore store = new(Path.Combine(_root, "desktop-settings.json"), [_root]);
        store.Save(new DesktopSettings(new Uri("http://example.test/"), "saved-api-key-0000"));

        store.Reset();

        Assert.False(store.Load().IsSaved);
    }

    [Fact]
    public void An_unreadable_settings_file_falls_back_to_the_defaults()
    {
        string path = Path.Combine(_root, "desktop-settings.json");
        File.WriteAllText(path, "{ not json");

        LoadedSettings loaded = new SettingsStore(path, [_root]).Load();

        Assert.False(loaded.IsSaved);
        Assert.Contains("could not be read", loaded.Source);
    }

    [Fact]
    public void The_admin_key_comes_from_the_env_file_unless_one_is_saved()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "Auth__ApiKey=from-env-file-0000\nAuth__AdminApiKey=admin-from-env-0000\n");
        string path = Path.Combine(_root, "settings", "desktop-settings.json");
        SettingsStore store = new(path, [_root]);

        Assert.Equal("admin-from-env-0000", store.Load().Settings.AdminApiKey);

        store.Save(new DesktopSettings(new Uri("http://example.test/"), "saved-api-key-0000"));
        Assert.Equal("admin-from-env-0000", store.Load().Settings.AdminApiKey);

        store.Save(new DesktopSettings(new Uri("http://example.test/"), "saved-api-key-0000", "saved-admin-key-0000"));
        Assert.Equal("saved-admin-key-0000", store.Load().Settings.AdminApiKey);
        Assert.DoesNotContain("saved-admin-key-0000", File.ReadAllText(path));
    }

    [Fact]
    public void Fyers_details_are_prefilled_from_the_env_file()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "Fyers__AppId=ABCDEFGHIJ-100\nFyers__SecretId=env-secret\nFyers__Pin=\n");
        SettingsStore store = new(Path.Combine(_root, "settings", "desktop-settings.json"), [_root]);

        LoadedFyersLogin loaded = store.LoadFyersLogin();

        Assert.False(loaded.IsRemembered);
        Assert.Equal(new FyersLoginDetails("ABCDEFGHIJ-100", "env-secret", null, "http://127.0.0.1:8085/callback"), loaded.Details);
        Assert.Contains(".env", loaded.Source);
    }

    [Fact]
    public void Remembered_fyers_details_round_trip_with_the_secret_and_pin_encrypted_and_can_be_forgotten()
    {
        SettingsStore store = new(Path.Combine(_root, "settings", "desktop-settings.json"), [_root]);
        FyersLoginDetails details = new("ABCDEFGHIJ-100", "my-secret-id", "4321", "http://127.0.0.1:9000/callback");

        store.SaveFyersLogin(details);
        LoadedFyersLogin loaded = store.LoadFyersLogin();

        Assert.True(loaded.IsRemembered);
        Assert.Equal(details, loaded.Details);
        string saved = File.ReadAllText(store.FyersLoginPath);
        Assert.DoesNotContain("my-secret-id", saved);
        Assert.DoesNotContain("4321", saved);

        store.ForgetFyersLogin();

        Assert.False(File.Exists(store.FyersLoginPath));
        Assert.Equal(new FyersLoginDetails(null, null, null, "http://127.0.0.1:8085/callback"), store.LoadFyersLogin().Details);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", "http://127.0.0.1:8080/")]
    [InlineData(" https://quotes.example.com/base ", "https://quotes.example.com/base/")]
    [InlineData("http://127.0.0.1:8080/", "http://127.0.0.1:8080/")]
    public void Base_addresses_get_a_trailing_slash(string text, string expected)
    {
        Assert.True(DesktopSettings.TryParseBaseAddress(text, out Uri address));
        Assert.Equal(expected, address.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1:8080")]
    [InlineData("ftp://example.com/")]
    [InlineData("/api")]
    public void Base_addresses_must_be_absolute_http_urls(string text)
    {
        Assert.False(DesktopSettings.TryParseBaseAddress(text, out _));
    }
}

using System.Net.Http;
using Microsoft.Extensions.Options;
using QuoteSnap.Client;
using QuoteSnap.Desktop.Settings;

namespace QuoteSnap.Desktop.Services;

/// <summary>The client for the current settings; replaced when the settings change.</summary>
public sealed class QuoteSnapConnection : IDisposable
{
    private HttpClient _http;

    public QuoteSnapConnection(DesktopSettings settings)
    {
        (_http, Client) = Create(settings);
        Settings = settings;
    }

    public event EventHandler? Changed;

    public DesktopSettings Settings { get; private set; }

    public QuoteSnapClient Client { get; private set; }

    public void Apply(DesktopSettings settings)
    {
        HttpClient previous = _http;
        (_http, Client) = Create(settings);
        Settings = settings;
        previous.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static (HttpClient Http, QuoteSnapClient Client) Create(DesktopSettings settings)
    {
        HttpClient http = new() { BaseAddress = settings.BaseAddress, Timeout = TimeSpan.FromMinutes(2) };
        QuoteSnapClient client = new(http, Options.Create(new QuoteSnapClientOptions
        {
            BaseAddress = settings.BaseAddress,
            ApiKey = settings.ApiKey,
            AdminApiKey = settings.AdminApiKey,
        }));
        return (http, client);
    }

    public void Dispose() => _http.Dispose();
}

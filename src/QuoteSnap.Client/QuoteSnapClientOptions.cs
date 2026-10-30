namespace QuoteSnap.Client;

public sealed class QuoteSnapClientOptions
{
    public Uri BaseAddress { get; set; } = new("http://127.0.0.1:8080/");

    /// <summary>Sent as X-Api-Key on /api calls.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Sent as X-Admin-Key on /admin calls. Only needed for the Fyers login.</summary>
    public string? AdminApiKey { get; set; }

    /// <summary>Used when the server doesn't send a Retry-After header.</summary>
    public TimeSpan DefaultPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MinPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(10);
}

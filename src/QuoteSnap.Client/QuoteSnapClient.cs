using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QuoteSnap.Contracts;

namespace QuoteSnap.Client;

public sealed class QuoteSnapClient
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly HttpClient _http;
    private readonly QuoteSnapClientOptions _options;

    public QuoteSnapClient(HttpClient http, IOptions<QuoteSnapClientOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress ??= _options.BaseAddress;
    }

    public async Task<QuoteRequestAccepted> SubmitAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        JsonContent body = JsonContent.Create(new CreateQuoteRequest(symbols.ToList()), options: Json);
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "api/quote-requests", body, admin: false, cancellationToken);
        return await ReadAsync<QuoteRequestAccepted>(response, cancellationToken);
    }

    public async Task<QuoteRequestDetails> GetAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        (await GetWithRetryAfterAsync(requestId, cancellationToken)).Details;

    public async Task<QuoteRequestPage> ListAsync(
        int limit = 20,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        string path = $"api/quote-requests?limit={limit}";
        if (before is { } b)
            path += $"&before={Uri.EscapeDataString(b.ToString("O"))}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        return await ReadAsync<QuoteRequestPage>(response, cancellationToken);
    }

    public async Task<ServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/status", null, admin: false, cancellationToken);
        return await ReadAsync<ServiceStatus>(response, cancellationToken);
    }

    /// <summary>Latest mutual fund NAVs by growth, payout or reinvestment ISIN, from AMFI's daily file.</summary>
    public async Task<NavLookup> GetNavsAsync(IEnumerable<string> isins, CancellationToken cancellationToken = default)
    {
        string path = $"api/nav?isin={string.Join(',', isins.Select(Uri.EscapeDataString))}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        return await ReadAsync<NavLookup>(response, cancellationToken);
    }

    /// <summary>Downloads AMFI's latest NAV file into the service, replacing the stored NAVs. Takes a few seconds.</summary>
    public async Task<NavRefreshResult> RefreshNavsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "api/nav/refresh", null, admin: false, cancellationToken);
        return await ReadAsync<NavRefreshResult>(response, cancellationToken);
    }

    /// <summary>
    /// Polls until the request finishes, waiting as long as the server's Retry-After header suggests.
    /// Use <paramref name="cancellationToken"/> to put an upper bound on the wait.
    /// </summary>
    public async Task<QuoteRequestDetails> WaitForCompletionAsync(
        Guid requestId,
        IProgress<QuoteRequestDetails>? progress = null,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            (QuoteRequestDetails details, TimeSpan? retryAfter) = await GetWithRetryAfterAsync(requestId, cancellationToken);
            progress?.Report(details);
            if (details.Status.IsTerminal())
                return details;

            await Task.Delay(ClampPollInterval(retryAfter ?? _options.DefaultPollInterval), cancellationToken);
        }
    }

    /// <summary>Submits symbols and waits for the finished snapshot.</summary>
    public async Task<QuoteRequestDetails> FetchSnapshotAsync(
        IEnumerable<string> symbols,
        IProgress<QuoteRequestDetails>? progress = null,
        CancellationToken cancellationToken = default)
    {
        QuoteRequestAccepted accepted = await SubmitAsync(symbols, cancellationToken);
        return await WaitForCompletionAsync(accepted.RequestId, progress, cancellationToken);
    }

    public async Task<FyersLoginUrl> GetFyersLoginUrlAsync(
        string appId,
        string redirectUri,
        string state,
        CancellationToken cancellationToken = default)
    {
        string path = $"admin/fyers/login-url?appId={Uri.EscapeDataString(appId)}"
            + $"&redirectUri={Uri.EscapeDataString(redirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: true, cancellationToken);
        return await ReadAsync<FyersLoginUrl>(response, cancellationToken);
    }

    /// <summary>Hands the auth code and the app credentials to the service, which stores them with the new tokens.</summary>
    public async Task<FyersSessionInfo> SubmitFyersLoginAsync(
        FyersAppCredentials app,
        string authCode,
        CancellationToken cancellationToken = default)
    {
        JsonContent body = JsonContent.Create(new SubmitFyersLogin(app, authCode), options: Json);
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "admin/fyers/session", body, admin: true, cancellationToken);
        return await ReadAsync<FyersSessionInfo>(response, cancellationToken);
    }

    public async Task<FyersSessionInfo> GetFyersSessionAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "admin/fyers/session", null, admin: true, cancellationToken);
        return await ReadAsync<FyersSessionInfo>(response, cancellationToken);
    }

    private async Task<(QuoteRequestDetails Details, TimeSpan? RetryAfter)> GetWithRetryAfterAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"api/quote-requests/{requestId}", null, admin: false, cancellationToken);
        TimeSpan? retryAfter = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => (TimeSpan?)null,
        };
        return (await ReadAsync<QuoteRequestDetails>(response, cancellationToken), retryAfter);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        bool admin,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, path) { Content = content };
        if (admin)
        {
            string key = _options.AdminApiKey
                ?? throw new InvalidOperationException("QuoteSnapClientOptions.AdminApiKey is required for admin calls.");
            request.Headers.Add(QuoteSnapHeaders.AdminKey, key);
        }
        else if (_options.ApiKey is { } key)
        {
            request.Headers.Add(QuoteSnapHeaders.ApiKey, key);
        }

        HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;

        using (response)
            throw await QuoteSnapApiException.FromResponseAsync(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
        ?? throw new QuoteSnapApiException(response.StatusCode, "Empty response", null);

    private TimeSpan ClampPollInterval(TimeSpan interval) =>
        interval < _options.MinPollInterval ? _options.MinPollInterval
        : interval > _options.MaxPollInterval ? _options.MaxPollInterval
        : interval;
}

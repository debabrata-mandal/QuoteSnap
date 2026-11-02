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
        (await GetWithRetryAfterAsync(requestId, cancellationToken)).Value;

    public async Task<QuoteRequestPage> ListAsync(int limit = 20, DateTimeOffset? before = null, CancellationToken cancellationToken = default)
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

    /// <summary>Finds Direct Plan schemes by loosely typed name ("icici pru large mid"), ISIN or scheme code.</summary>
    public async Task<NavSearchResult> SearchNavsAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        string path = $"api/nav/search?q={Uri.EscapeDataString(query)}&limit={limit}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        return await ReadAsync<NavSearchResult>(response, cancellationToken);
    }

    /// <summary>Latest NAVs by AMFI scheme code, in the order given; unknown codes are listed in NotFound.</summary>
    public async Task<NavLookup> GetNavsBySchemeCodeAsync(IEnumerable<int> schemeCodes, CancellationToken cancellationToken = default)
    {
        string path = $"api/nav/schemes?code={string.Join(',', schemeCodes)}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        return await ReadAsync<NavLookup>(response, cancellationToken);
    }

    /// <summary>The user's fund list, in the user's order, with their latest NAVs.</summary>
    public async Task<MyFundList> GetMyFundsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/my-funds", null, admin: false, cancellationToken);
        return await ReadAsync<MyFundList>(response, cancellationToken);
    }

    /// <summary>
    /// Today's estimated NAV of each listed fund, from its holdings and the last finished holdings quote request
    /// (<see cref="StartHoldingQuotesAsync"/>).
    /// </summary>
    public async Task<NavEstimateList> GetNavEstimatesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/my-funds/estimates", null, admin: false, cancellationToken);
        return await ReadAsync<NavEstimateList>(response, cancellationToken);
    }

    /// <summary>Adds a scheme to the user's list; adding one that's already there returns it unchanged.</summary>
    public async Task<MyFund> AddMyFundAsync(int schemeCode, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, $"api/my-funds/{schemeCode}", null, admin: false, cancellationToken);
        return await ReadAsync<MyFund>(response, cancellationToken);
    }

    /// <summary>Removes a scheme from the user's list; removing one that isn't there also succeeds.</summary>
    public async Task RemoveMyFundAsync(int schemeCode, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"api/my-funds/{schemeCode}", null, admin: false, cancellationToken);
    }

    /// <summary>Rearranges the user's list. <paramref name="schemeCodes"/> must name every fund on it exactly once; otherwise the API answers 409 Conflict.</summary>
    public async Task ReorderMyFundsAsync(IReadOnlyList<int> schemeCodes, CancellationToken cancellationToken = default)
    {
        JsonContent body = JsonContent.Create(new MyFundOrder(schemeCodes), options: Json);
        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, "api/my-funds/order", body, admin: false, cancellationToken);
    }

    public async Task<NavStatus> GetNavStatusAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/nav/status", null, admin: false, cancellationToken);
        return await ReadAsync<NavStatus>(response, cancellationToken);
    }

    /// <summary>Downloads AMFI's latest NAV file into the service, replacing the stored NAVs. Takes a few seconds.</summary>
    public async Task<NavRefreshResult> RefreshNavsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "api/nav/refresh", null, admin: false, cancellationToken);
        return await ReadAsync<NavRefreshResult>(response, cancellationToken);
    }

    public async Task<FyersSymbolStatus> GetSymbolStatusAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/symbols/status", null, admin: false, cancellationToken);
        return await ReadAsync<FyersSymbolStatus>(response, cancellationToken);
    }

    /// <summary>Finds listed stocks by company name, ticker or ISIN, to get their Fyers symbols.</summary>
    public async Task<FyersSymbolSearchResult> SearchSymbolsAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        string path = $"api/symbols/search?q={Uri.EscapeDataString(query)}&limit={limit}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        return await ReadAsync<FyersSymbolSearchResult>(response, cancellationToken);
    }

    /// <summary>
    /// Downloads Fyers' NSE and BSE symbol masters into the service, replacing the stored symbols that symbol search and
    /// holdings fetches use. Takes a few seconds.
    /// </summary>
    public async Task<FyersSymbolRefreshResult> RefreshSymbolsAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "api/symbols/refresh", null, admin: false, cancellationToken);
        return await ReadAsync<FyersSymbolRefreshResult>(response, cancellationToken);
    }

    /// <summary>
    /// Starts scraping the holdings of every listed fund from ScanX; poll it with <see cref="GetHoldingFetchAsync"/>.
    /// While one is already queued or running, that one is returned instead.
    /// </summary>
    public async Task<HoldingFetch> StartHoldingFetchAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "api/holdings/fetches", null, admin: false, cancellationToken);
        return await ReadAsync<HoldingFetch>(response, cancellationToken);
    }

    public async Task<HoldingFetch> GetHoldingFetchAsync(Guid fetchId, CancellationToken cancellationToken = default) =>
        (await GetWithRetryAfterAsync<HoldingFetch>($"api/holdings/fetches/{fetchId}", cancellationToken)).Value;

    /// <summary>The most recently started holdings fetch; null when holdings have never been fetched.</summary>
    public async Task<HoldingFetch?> GetLatestHoldingFetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await GetWithRetryAfterAsync<HoldingFetch>("api/holdings/fetches/latest", cancellationToken)).Value;
        }
        catch (QuoteSnapApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Requests quotes for the distinct stock symbols in every listed fund's latest holdings. Returns the running
    /// request instead when one is already queued or running. Wait for it with <see cref="WaitForCompletionAsync"/>.
    /// </summary>
    public async Task<QuoteRequestAccepted> StartHoldingQuotesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "api/holdings/quotes", null, admin: false, cancellationToken);
        return await ReadAsync<QuoteRequestAccepted>(response, cancellationToken);
    }

    /// <summary>The most recent quote request for the holdings, with results; null when none has been made.</summary>
    public async Task<QuoteRequestDetails?> GetLatestHoldingQuotesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await GetWithRetryAfterAsync<QuoteRequestDetails>("api/holdings/quotes/latest", cancellationToken)).Value;
        }
        catch (QuoteSnapApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>The stocks the listed funds hold most, by weight averaged over the funds, with the last live prices.</summary>
    /// <param name="limit">How many stocks, 1 to 100.</param>
    public async Task<TopHoldingList> GetTopHoldingsAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"api/holdings/top?limit={limit}", null, admin: false, cancellationToken);
        return await ReadAsync<TopHoldingList>(response, cancellationToken);
    }

    /// <summary>The latest streamed tick of each top holding, largest weight first.</summary>
    public async Task<LiveQuoteList> GetLiveQuotesAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/live/quotes", null, admin: false, cancellationToken);
        return await ReadAsync<LiveQuoteList>(response, cancellationToken);
    }

    public async Task<LiveFeedStatus> GetLiveFeedStatusAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "api/live/status", null, admin: false, cancellationToken);
        return await ReadAsync<LiveFeedStatus>(response, cancellationToken);
    }

    /// <summary>Polls a holdings fetch until it finishes, reporting each snapshot of its progress.</summary>
    public async Task<HoldingFetch> WaitForHoldingFetchAsync(Guid fetchId, IProgress<HoldingFetch>? progress = null, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            (HoldingFetch fetch, TimeSpan? retryAfter) = await GetWithRetryAfterAsync<HoldingFetch>($"api/holdings/fetches/{fetchId}", cancellationToken);
            progress?.Report(fetch);
            if (fetch.Status.IsTerminal())
                return fetch;

            await Task.Delay(ClampPollInterval(retryAfter ?? _options.DefaultPollInterval), cancellationToken);
        }
    }

    /// <summary>A listed fund's stored portfolio.</summary>
    /// <param name="month">Any day in the month to show; the latest stored month when null.</param>
    public async Task<FundHoldings> GetFundHoldingsAsync(int schemeCode, DateOnly? month = null, CancellationToken cancellationToken = default)
    {
        string path = $"api/my-funds/{schemeCode}/holdings";
        if (month is { } m)
            path += $"?month={m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        return await ReadAsync<FundHoldings>(response, cancellationToken);
    }

    /// <summary>
    /// Polls until the request finishes, waiting as long as the server's Retry-After header suggests.
    /// Use <paramref name="cancellationToken"/> to put an upper bound on the wait.
    /// </summary>
    public async Task<QuoteRequestDetails> WaitForCompletionAsync(Guid requestId, IProgress<QuoteRequestDetails>? progress = null, CancellationToken cancellationToken = default)
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
    public async Task<QuoteRequestDetails> FetchSnapshotAsync(IEnumerable<string> symbols, IProgress<QuoteRequestDetails>? progress = null, CancellationToken cancellationToken = default)
    {
        QuoteRequestAccepted accepted = await SubmitAsync(symbols, cancellationToken);
        return await WaitForCompletionAsync(accepted.RequestId, progress, cancellationToken);
    }

    public async Task<FyersLoginUrl> GetFyersLoginUrlAsync(string appId, string redirectUri, string state, CancellationToken cancellationToken = default)
    {
        string path = $"admin/fyers/login-url?appId={Uri.EscapeDataString(appId)}"
            + $"&redirectUri={Uri.EscapeDataString(redirectUri)}"
            + $"&state={Uri.EscapeDataString(state)}";
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: true, cancellationToken);
        return await ReadAsync<FyersLoginUrl>(response, cancellationToken);
    }

    /// <summary>Hands the auth code and the app credentials to the service, which stores them with the new tokens.</summary>
    public async Task<FyersSessionInfo> SubmitFyersLoginAsync(FyersAppCredentials app, string authCode, CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Asks Fyers to revoke the access token and makes the service forget the login; quote requests are refused until
    /// the next login.
    /// </summary>
    public async Task<FyersLogoutResult> LogoutFyersAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, "admin/fyers/session", null, admin: true, cancellationToken);
        return await ReadAsync<FyersLogoutResult>(response, cancellationToken);
    }

    private Task<(QuoteRequestDetails Value, TimeSpan? RetryAfter)> GetWithRetryAfterAsync(Guid requestId, CancellationToken cancellationToken) =>
        GetWithRetryAfterAsync<QuoteRequestDetails>($"api/quote-requests/{requestId}", cancellationToken);

    private async Task<(T Value, TimeSpan? RetryAfter)> GetWithRetryAfterAsync<T>(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, admin: false, cancellationToken);
        TimeSpan? retryAfter = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => (TimeSpan?)null,
        };
        return (await ReadAsync<T>(response, cancellationToken), retryAfter);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, bool admin, CancellationToken cancellationToken)
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

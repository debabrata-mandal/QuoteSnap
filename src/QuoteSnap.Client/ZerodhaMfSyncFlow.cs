using System.Net;
using System.Security.Cryptography;
using QuoteSnap.Contracts;

namespace QuoteSnap.Client;

public sealed class ZerodhaLoginException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class ZerodhaLoginOptions
{
    public const string DefaultRedirectUri = "http://127.0.0.1:8085/zerodha/callback";

    /// <summary>Must match the redirect URL registered on the Kite Connect app exactly.</summary>
    public string RedirectUri { get; set; } = DefaultRedirectUri;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public Action<string> OpenBrowser { get; set; } = FyersLoginOptions.DefaultOpenBrowser;
}

/// <summary>
/// Syncs the fund list with the Zerodha mutual fund holdings: runs the Kite browser login, then hands the one-time
/// request token to the QuoteSnap service, which reads the holdings and drops the token. No Zerodha login is kept.
/// </summary>
public sealed class ZerodhaMfSyncFlow(QuoteSnapClient client)
{
    public async Task<ZerodhaMfSyncResult> SyncWithBrowserLoginAsync(ZerodhaAppCredentials app, ZerodhaLoginOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ZerodhaLoginOptions();
        Uri callback = GetLoopbackCallback(options.RedirectUri);
        string state = CreateState();
        string loginUrl = (await client.GetZerodhaLoginUrlAsync(app.ApiKey, state, cancellationToken)).Url;

        Uri redirect;
        using (LoopbackCallbackListener listener = LoopbackCallbackListener.Start(callback.Port, callback.AbsolutePath, "Zerodha", (message, ex) => new ZerodhaLoginException(message, ex)))
        {
            options.OpenBrowser(loginUrl);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            try
            {
                redirect = await listener.WaitForCallbackAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ZerodhaLoginException($"No login callback arrived within {options.Timeout.TotalMinutes:0} minutes. Nothing was changed.");
            }
        }

        return await client.SyncZerodhaMutualFundsAsync(app, ParseRedirect(redirect, state), cancellationToken);
    }

    /// <summary>Extracts the request token from the Kite redirect after checking it belongs to this login attempt.</summary>
    public static string ParseRedirect(Uri redirect, string expectedState)
    {
        Dictionary<string, string> query = ParseQuery(redirect.Query);

        if (query.TryGetValue("status", out string? status) && !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            throw new ZerodhaLoginException($"Zerodha reported a login error: {query.GetValueOrDefault("message") ?? status}");

        if (!query.TryGetValue("state", out string? state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
            throw new ZerodhaLoginException("The login response doesn't match this login attempt. Start the sync again.");

        if (!query.TryGetValue("request_token", out string? requestToken) || string.IsNullOrWhiteSpace(requestToken))
            throw new ZerodhaLoginException("The Zerodha redirect didn't include a request_token.");

        return requestToken;
    }

    private static Uri GetLoopbackCallback(string redirectUri)
    {
        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host == IPAddress.Loopback.ToString()))
            return uri;

        throw new ZerodhaLoginException($"The redirect URL '{redirectUri}' isn't a local http:// address, so the browser login can't receive it.");
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = separator < 0 ? pair : pair[..separator];
            string value = separator < 0 ? "" : pair[(separator + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    private static string CreateState() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}

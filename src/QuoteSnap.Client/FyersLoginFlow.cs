using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using QuoteSnap.Contracts;

namespace QuoteSnap.Client;

public sealed class FyersLoginException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class FyersLoginOptions
{
    public const string DefaultRedirectUri = "http://127.0.0.1:8085/callback";

    /// <summary>Must match the redirect URI registered on the Fyers app exactly.</summary>
    public string RedirectUri { get; set; } = DefaultRedirectUri;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public Action<string> OpenBrowser { get; set; } = DefaultOpenBrowser;

    public static void DefaultOpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// <summary>
/// Runs the Fyers browser login and hands the auth code and app credentials to the QuoteSnap service, which
/// exchanges the code for tokens and keeps everything it needs for later requests.
/// </summary>
public sealed class FyersLoginFlow(QuoteSnapClient client)
{
    public async Task<FyersSessionInfo> LoginWithBrowserAsync(
        FyersAppCredentials app,
        FyersLoginOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FyersLoginOptions();
        Uri callback = GetLoopbackCallback(options.RedirectUri);
        string state = CreateState();
        string loginUrl = (await client.GetFyersLoginUrlAsync(app.AppId, options.RedirectUri, state, cancellationToken)).Url;

        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start(callback.Port, callback.AbsolutePath);
        options.OpenBrowser(loginUrl);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        Uri redirect;
        try
        {
            redirect = await listener.WaitForCallbackAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FyersLoginException(
                $"No login callback arrived within {options.Timeout.TotalMinutes:0} minutes. Nothing was changed.");
        }

        return await client.SubmitFyersLoginAsync(app, ParseRedirect(redirect, state), cancellationToken);
    }

    /// <summary>
    /// For when no local listener can run: <paramref name="getRedirectUrl"/> receives the login URL and must return
    /// the full URL the browser was redirected to after logging in.
    /// </summary>
    public async Task<FyersSessionInfo> LoginManualAsync(
        FyersAppCredentials app,
        Func<string, CancellationToken, Task<string>> getRedirectUrl,
        string redirectUri = FyersLoginOptions.DefaultRedirectUri,
        CancellationToken cancellationToken = default)
    {
        string state = CreateState();
        string loginUrl = (await client.GetFyersLoginUrlAsync(app.AppId, redirectUri, state, cancellationToken)).Url;
        string pasted = (await getRedirectUrl(loginUrl, cancellationToken)).Trim();
        if (!Uri.TryCreate(pasted, UriKind.Absolute, out Uri? redirect))
            throw new FyersLoginException("That isn't a full URL. Copy the whole address from the browser after logging in.");

        return await client.SubmitFyersLoginAsync(app, ParseRedirect(redirect, state), cancellationToken);
    }

    /// <summary>Extracts the auth code from the Fyers redirect after checking it belongs to this login attempt.</summary>
    public static string ParseRedirect(Uri redirect, string expectedState)
    {
        Dictionary<string, string> query = ParseQuery(redirect.Query);

        if (query.TryGetValue("s", out string? status) && !string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            throw new FyersLoginException($"Fyers reported a login error: {query.GetValueOrDefault("message") ?? status}");

        if (!query.TryGetValue("state", out string? state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
            throw new FyersLoginException("The login response doesn't match this login attempt. Start the login again.");

        if (!query.TryGetValue("auth_code", out string? authCode) || string.IsNullOrWhiteSpace(authCode))
            throw new FyersLoginException("The Fyers redirect didn't include an auth_code.");

        return authCode;
    }

    private static Uri GetLoopbackCallback(string redirectUri)
    {
        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || uri.Host == IPAddress.Loopback.ToString()))
        {
            return uri;
        }

        throw new FyersLoginException(
            $"The redirect URI '{redirectUri}' isn't a local http:// address, so the browser login can't receive it. "
            + "Use manual mode instead.");
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

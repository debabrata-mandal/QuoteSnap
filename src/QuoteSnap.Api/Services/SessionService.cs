using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

public sealed class FyersReloginRequiredException(string message) : Exception(message)
{
    public const string DefaultMessage =
        "Fyers re-authentication required. Run the QuoteSnap login tool (quotesnap-login login).";

    public FyersReloginRequiredException() : this(DefaultMessage)
    {
    }
}

public sealed class FyersAccountMismatchException(string message) : Exception(message);

/// <summary>An access token, the Fyers app it belongs to, and the session version it was read from.</summary>
public sealed record AccessTokenLease(string AppId, string Token, int Version);

/// <summary>Owns the Fyers session lifecycle: login, on-demand refresh, and status.</summary>
public sealed class SessionService(
    IServiceScopeFactory scopes,
    IOptions<FyersOptions> options,
    TimeProvider time,
    ILogger<SessionService> logger)
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FallbackAccessTokenLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan FallbackRefreshTokenLifetime = TimeSpan.FromDays(15);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public string BuildLoginUrl(string appId, string redirectUri, string state) =>
        FyersAuthClient.BuildLoginUrl(options.Value, appId, redirectUri, state);

    public async Task<FyersSessionInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        FyersSession? session = await scope.ServiceProvider.GetRequiredService<SessionStore>().GetAsync(cancellationToken);
        return ToInfo(session);
    }

    public async Task<AccessTokenLease> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        FyersSession? session;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
            session = await scope.ServiceProvider.GetRequiredService<SessionStore>().GetAsync(cancellationToken);

        if (session is null)
            throw new FyersReloginRequiredException();

        return IsAccessTokenUsable(session)
            ? ToLease(session)
            : await RefreshAsync(session.Version, cancellationToken);
    }

    /// <summary>
    /// Gets a new access token. <paramref name="staleVersion"/> is the session version whose token failed or expired;
    /// if the session has moved on since then, the newer token is returned without calling Fyers.
    /// </summary>
    public async Task<AccessTokenLease> RefreshAsync(int staleVersion, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            SessionStore store = scope.ServiceProvider.GetRequiredService<SessionStore>();
            FyersAuthClient auth = scope.ServiceProvider.GetRequiredService<FyersAuthClient>();

            FyersSession session = await store.GetAsync(cancellationToken) ?? throw new FyersReloginRequiredException();
            if (session.Version != staleVersion && IsAccessTokenUsable(session))
                return ToLease(session);

            if (session.RefreshTokenExpiresAt <= time.GetUtcNow())
                throw new FyersReloginRequiredException();

            string accessToken;
            try
            {
                accessToken = await auth.RefreshAccessTokenAsync(session.App, session.RefreshToken, cancellationToken);
            }
            catch (FyersApiException ex) when (IsRejection(ex))
            {
                logger.LogWarning("Fyers rejected the refresh token: {Reason}", ex.Message);
                await store.TryInvalidateRefreshTokenAsync(session.Version, cancellationToken);
                throw new FyersReloginRequiredException();
            }

            DateTimeOffset expiresAt = FyersTokens.ReadJwtExpiry(accessToken) ?? time.GetUtcNow() + FallbackAccessTokenLifetime;
            if (await store.TryUpdateAccessTokenAsync(session.Version, accessToken, expiresAt, cancellationToken))
            {
                logger.LogInformation("Fyers access token refreshed; valid until {ExpiresAt}", expiresAt);
                return new AccessTokenLease(session.App.AppId, accessToken, session.Version + 1);
            }

            // A new login was stored while we were refreshing; it takes precedence.
            FyersSession? latest = await store.GetAsync(cancellationToken);
            return latest is not null && IsAccessTokenUsable(latest)
                ? ToLease(latest)
                : throw new FyersReloginRequiredException();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Exchanges the auth code using <paramref name="app"/>, then stores the credentials with the new tokens so later
    /// quote requests and daily refreshes need nothing else.
    /// </summary>
    public async Task<FyersSessionInfo> CompleteLoginAsync(
        FyersAppCredentials app,
        string authCode,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        SessionStore store = scope.ServiceProvider.GetRequiredService<SessionStore>();
        FyersAuthClient auth = scope.ServiceProvider.GetRequiredService<FyersAuthClient>();

        FyersLoginTokens tokens = await auth.ExchangeAuthCodeAsync(app, authCode, cancellationToken);
        FyersProfile profile = await auth.GetProfileAsync(app.AppId, tokens.AccessToken, cancellationToken);

        string? expected = options.Value.ExpectedFyersId;
        if (!string.IsNullOrWhiteSpace(expected)
            && !string.Equals(profile.FyersId, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new FyersAccountMismatchException(
                $"Logged in as {profile.FyersId}, but this service is configured for {expected}. The login was not stored.");
        }

        DateTimeOffset now = time.GetUtcNow();
        await store.SaveLoginAsync(
            new FyersSession(
                profile.FyersId,
                app,
                tokens.AccessToken,
                tokens.RefreshToken,
                FyersTokens.ReadJwtExpiry(tokens.AccessToken) ?? now + FallbackAccessTokenLifetime,
                FyersTokens.ReadJwtExpiry(tokens.RefreshToken) ?? now + FallbackRefreshTokenLifetime,
                now,
                Version: 0),
            cancellationToken);

        logger.LogInformation("Stored a new Fyers login for {FyersId}", profile.FyersId);
        return ToInfo(await store.GetAsync(cancellationToken));
    }

    private FyersSessionInfo ToInfo(FyersSession? session)
    {
        if (session is null)
            return new FyersSessionInfo(FyersAuthState.ReloginRequired, null, null, null, null);

        DateTimeOffset now = time.GetUtcNow();
        FyersAuthState state = session.AccessTokenExpiresAt > now ? FyersAuthState.Ok
            : session.RefreshTokenExpiresAt > now ? FyersAuthState.AccessTokenExpired
            : FyersAuthState.ReloginRequired;

        return new FyersSessionInfo(
            state,
            session.FyersId,
            session.AccessTokenExpiresAt,
            session.RefreshTokenExpiresAt,
            session.UpdatedAt);
    }

    private static AccessTokenLease ToLease(FyersSession session) =>
        new(session.App.AppId, session.AccessToken, session.Version);

    private bool IsAccessTokenUsable(FyersSession session) =>
        session.AccessTokenExpiresAt - ExpirySkew > time.GetUtcNow();

    // A definite "no" from Fyers, as opposed to an outage or throttling that may clear up by itself.
    private static bool IsRejection(FyersApiException ex) =>
        ex is FyersAuthException || ex is not FyersRateLimitException && ex.HttpStatus is null or < 500;
}

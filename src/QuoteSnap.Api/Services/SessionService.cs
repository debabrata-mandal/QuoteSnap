using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

public sealed class FyersReloginRequiredException(string message) : Exception(message)
{
    public const string DefaultMessage =
        "Fyers re-authentication required. Log in on the desktop app's Fyers login page or with the QuoteSnap login tool (quotesnap-login login).";

    public FyersReloginRequiredException() : this(DefaultMessage)
    {
    }
}

public sealed class FyersAccountMismatchException(string message) : Exception(message);

/// <summary>An access token, the Fyers app it belongs to, and the session version it was read from.</summary>
public sealed record AccessTokenLease(string AppId, string Token, int Version);

/// <summary>Owns the Fyers session lifecycle: login, on-demand refresh, and status.</summary>
public sealed class SessionService(IServiceScopeFactory scopes, IOptions<FyersOptions> options, TimeProvider time, ILogger<SessionService> logger)
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
        {
            logger.LogWarning("No Fyers session is stored; run the login tool");
            throw new FyersReloginRequiredException();
        }

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
            {
                logger.LogDebug("Another caller already refreshed the Fyers access token; using it");
                return ToLease(session);
            }

            if (session.RefreshTokenExpiresAt <= time.GetUtcNow())
            {
                logger.LogWarning("The Fyers refresh token for {FyersId} expired at {RefreshTokenExpiresAt}; run the login tool", session.FyersId, session.RefreshTokenExpiresAt);
                throw new FyersReloginRequiredException();
            }

            logger.LogInformation("Refreshing the Fyers access token for {FyersId} (current token valid until {AccessTokenExpiresAt})", session.FyersId, session.AccessTokenExpiresAt);
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
    public async Task<FyersSessionInfo> CompleteLoginAsync(FyersAppCredentials app, string authCode, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        SessionStore store = scope.ServiceProvider.GetRequiredService<SessionStore>();
        FyersAuthClient auth = scope.ServiceProvider.GetRequiredService<FyersAuthClient>();

        logger.LogInformation("Exchanging a Fyers auth code for tokens (app {AppId})", app.AppId);
        FyersLoginTokens tokens = await auth.ExchangeAuthCodeAsync(app, authCode, cancellationToken);
        FyersProfile profile = await auth.GetProfileAsync(app.AppId, tokens.AccessToken, cancellationToken);

        string? expected = options.Value.ExpectedFyersId;
        if (!string.IsNullOrWhiteSpace(expected)
            && !string.Equals(profile.FyersId, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Rejected a Fyers login for {FyersId}: this service only accepts {ExpectedFyersId}", profile.FyersId, expected);
            throw new FyersAccountMismatchException($"Logged in as {profile.FyersId}, but this service is configured for {expected}. The login was not stored.");
        }

        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset refreshTokenExpiresAt = FyersTokens.ReadJwtExpiry(tokens.RefreshToken) ?? now + FallbackRefreshTokenLifetime;
        await store.SaveLoginAsync(
            new FyersSession(profile.FyersId, app, tokens.AccessToken, tokens.RefreshToken, FyersTokens.ReadJwtExpiry(tokens.AccessToken) ?? now + FallbackAccessTokenLifetime, refreshTokenExpiresAt, now, Version: 0),
            cancellationToken);

        logger.LogInformation("Stored a new Fyers login for {FyersId} (app {AppId}); refresh token valid until {RefreshTokenExpiresAt}", profile.FyersId, app.AppId, refreshTokenExpiresAt);
        return ToInfo(await store.GetAsync(cancellationToken));
    }

    /// <summary>
    /// Asks Fyers to revoke the access token, then forgets the login. The login is forgotten even when Fyers can't be
    /// reached, so the service can neither use nor renew it; the token then lapses at Fyers on its own.
    /// </summary>
    public async Task<FyersLogoutResult> LogoutAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            SessionStore store = scope.ServiceProvider.GetRequiredService<SessionStore>();
            FyersAuthClient auth = scope.ServiceProvider.GetRequiredService<FyersAuthClient>();

            FyersSession? session = await store.GetAsync(cancellationToken);
            if (session is null)
            {
                // Also clears a row that can no longer be decrypted.
                await store.DeleteAsync(cancellationToken);
                return new FyersLogoutResult(null, false, null);
            }

            bool revoked = true;
            string? error = null;
            if (session.AccessTokenExpiresAt > time.GetUtcNow())
            {
                try
                {
                    await auth.LogoutAsync(session.App.AppId, session.AccessToken, cancellationToken);
                }
                catch (FyersAuthException ex)
                {
                    logger.LogInformation("Fyers says the access token is already invalid: {Reason}", ex.Message);
                }
                catch (Exception ex) when (ex is FyersApiException or HttpRequestException or Polly.ExecutionRejectedException
                                           || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Fyers could not revoke the access token for {FyersId}: {Reason}", session.FyersId, ex.Message);
                    revoked = false;
                    error = ex.Message;
                }
            }

            await store.DeleteAsync(cancellationToken);
            logger.LogInformation("Logged out of Fyers: forgot the login for {FyersId} (app {AppId}); access token revoked at Fyers: {Revoked}", session.FyersId, session.App.AppId, revoked);
            return new FyersLogoutResult(session.FyersId, revoked, error);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private FyersSessionInfo ToInfo(FyersSession? session)
    {
        if (session is null)
            return new FyersSessionInfo(FyersAuthState.ReloginRequired, null, null, null, null);

        DateTimeOffset now = time.GetUtcNow();
        FyersAuthState state = session.AccessTokenExpiresAt > now ? FyersAuthState.Ok
            : session.RefreshTokenExpiresAt > now ? FyersAuthState.AccessTokenExpired
            : FyersAuthState.ReloginRequired;

        return new FyersSessionInfo(state, session.FyersId, session.AccessTokenExpiresAt, session.RefreshTokenExpiresAt, session.UpdatedAt);
    }

    private static AccessTokenLease ToLease(FyersSession session) =>
        new(session.App.AppId, session.AccessToken, session.Version);

    private bool IsAccessTokenUsable(FyersSession session) =>
        session.AccessTokenExpiresAt - ExpirySkew > time.GetUtcNow();

    // A definite "no" from Fyers, as opposed to an outage or throttling that may clear up by itself.
    private static bool IsRejection(FyersApiException ex) =>
        ex is FyersAuthException || ex is not FyersRateLimitException && ex.HttpStatus is null or < 500;
}

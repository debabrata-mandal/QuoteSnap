namespace QuoteSnap.Contracts;

public sealed record FyersSessionInfo(
    FyersAuthState State,
    string? FyersId,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt,
    DateTimeOffset? UpdatedAt);

public sealed record ServiceStatus(string Version, DateTimeOffset ServerTime, FyersSessionInfo Fyers);

public sealed record FyersLoginUrl(string Url);

/// <summary>The Fyers API app (from myapi.fyers.in) and the account PIN; the service stores them encrypted.</summary>
public sealed record FyersAppCredentials(string AppId, string SecretId, string Pin);

public sealed record SubmitFyersLogin(FyersAppCredentials App, string AuthCode);

/// <summary>What a logout did. The stored login is always forgotten, whether or not Fyers could be told.</summary>
/// <param name="FyersId">The account that was logged out; null when no login was stored.</param>
/// <param name="RevokedAtFyers">Whether the access token no longer works at Fyers: revoked now, or already expired or invalid.</param>
/// <param name="FyersError">Why Fyers couldn't be asked to revoke the access token, when it couldn't.</param>
public sealed record FyersLogoutResult(string? FyersId, bool RevokedAtFyers, string? FyersError);

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

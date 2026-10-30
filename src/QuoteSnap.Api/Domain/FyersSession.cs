using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Domain;

/// <summary>The decrypted Fyers session. <see cref="Version"/> increases on every write.</summary>
public sealed record FyersSession(
    string FyersId,
    FyersAppCredentials App,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    DateTimeOffset UpdatedAt,
    int Version);

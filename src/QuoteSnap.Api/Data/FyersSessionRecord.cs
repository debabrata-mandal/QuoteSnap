namespace QuoteSnap.Api.Data;

/// <summary>The single stored Fyers session row. Secrets and tokens are Data Protection ciphertext.</summary>
public sealed class FyersSessionRecord
{
    public int Id { get; set; }

    public required string FyersId { get; set; }

    public required string AppId { get; set; }

    public required string ProtectedSecretId { get; set; }

    public required string ProtectedPin { get; set; }

    public required string ProtectedAccessToken { get; set; }

    public required string ProtectedRefreshToken { get; set; }

    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    public DateTimeOffset RefreshTokenExpiresAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>"login" or "refresh".</summary>
    public required string Source { get; set; }

    public int Version { get; set; }
}

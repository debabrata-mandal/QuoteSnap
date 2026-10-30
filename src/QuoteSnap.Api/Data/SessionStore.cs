using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

public sealed class SessionStore(
    QuoteSnapDbContext db,
    IDataProtectionProvider dataProtection,
    TimeProvider time,
    ILogger<SessionStore> logger)
{
    private const int SessionId = 1;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("QuoteSnap.FyersSession.v1");

    public async Task<FyersSession?> GetAsync(CancellationToken cancellationToken)
    {
        FyersSessionRecord? record = await db.FyersSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == SessionId, cancellationToken);
        if (record is null)
            return null;

        try
        {
            return new FyersSession(
                record.FyersId,
                new FyersAppCredentials(
                    record.AppId,
                    _protector.Unprotect(record.ProtectedSecretId),
                    _protector.Unprotect(record.ProtectedPin)),
                _protector.Unprotect(record.ProtectedAccessToken),
                _protector.Unprotect(record.ProtectedRefreshToken),
                record.AccessTokenExpiresAt,
                record.RefreshTokenExpiresAt,
                record.UpdatedAt,
                record.Version);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "The stored Fyers session could not be decrypted; a new login is required");
            return null;
        }
    }

    /// <summary>Stores a fresh login. Always wins over whatever is stored.</summary>
    public async Task SaveLoginAsync(FyersSession session, CancellationToken cancellationToken)
    {
        string secretId = _protector.Protect(session.App.SecretId);
        string pin = _protector.Protect(session.App.Pin);
        string accessToken = _protector.Protect(session.AccessToken);
        string refreshToken = _protector.Protect(session.RefreshToken);
        DateTimeOffset now = time.GetUtcNow();

        int updated = await db.FyersSessions
            .Where(s => s.Id == SessionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.FyersId, session.FyersId)
                .SetProperty(s => s.AppId, session.App.AppId)
                .SetProperty(s => s.ProtectedSecretId, secretId)
                .SetProperty(s => s.ProtectedPin, pin)
                .SetProperty(s => s.ProtectedAccessToken, accessToken)
                .SetProperty(s => s.ProtectedRefreshToken, refreshToken)
                .SetProperty(s => s.AccessTokenExpiresAt, session.AccessTokenExpiresAt)
                .SetProperty(s => s.RefreshTokenExpiresAt, session.RefreshTokenExpiresAt)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.Source, "login")
                .SetProperty(s => s.Version, s => s.Version + 1),
                cancellationToken);

        if (updated > 0)
            return;

        db.FyersSessions.Add(new FyersSessionRecord
        {
            Id = SessionId,
            FyersId = session.FyersId,
            AppId = session.App.AppId,
            ProtectedSecretId = secretId,
            ProtectedPin = pin,
            ProtectedAccessToken = accessToken,
            ProtectedRefreshToken = refreshToken,
            AccessTokenExpiresAt = session.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = session.RefreshTokenExpiresAt,
            UpdatedAt = now,
            Source = "login",
            Version = 1,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Saves a refreshed access token only if the session is still at <paramref name="expectedVersion"/>,
    /// so a refresh that started before a new login can't overwrite it.
    /// </summary>
    public async Task<bool> TryUpdateAccessTokenAsync(
        int expectedVersion,
        string accessToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        string protectedToken = _protector.Protect(accessToken);
        DateTimeOffset now = time.GetUtcNow();

        int updated = await db.FyersSessions
            .Where(s => s.Id == SessionId && s.Version == expectedVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.ProtectedAccessToken, protectedToken)
                .SetProperty(s => s.AccessTokenExpiresAt, expiresAt)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.Source, "refresh")
                .SetProperty(s => s.Version, s => s.Version + 1),
                cancellationToken);
        return updated == 1;
    }

    /// <summary>Marks the refresh token as unusable after Fyers rejected it, so status reports a re-login.</summary>
    public async Task<bool> TryInvalidateRefreshTokenAsync(int expectedVersion, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        int updated = await db.FyersSessions
            .Where(s => s.Id == SessionId && s.Version == expectedVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RefreshTokenExpiresAt, now)
                .SetProperty(s => s.AccessTokenExpiresAt, now)
                .SetProperty(s => s.UpdatedAt, now)
                .SetProperty(s => s.Version, s => s.Version + 1),
                cancellationToken);
        return updated == 1;
    }
}

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuoteSnap.Api.Fyers;

public static class FyersTokens
{
    /// <summary>The <c>appIdHash</c> Fyers expects: SHA-256 of <c>appId:secretId</c>, lowercase hex.</summary>
    public static string AppIdHash(string appId, string secretId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{appId}:{secretId}")));

    /// <summary>Reads the <c>exp</c> claim of a JWT without validating it; null if the token isn't a readable JWT.</summary>
    public static DateTimeOffset? ReadJwtExpiry(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            using JsonDocument payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return payload.RootElement.TryGetProperty("exp", out JsonElement exp) && exp.TryGetInt64(out long seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

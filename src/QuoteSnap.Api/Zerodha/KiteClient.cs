using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace QuoteSnap.Api.Zerodha;

public sealed record KiteLogin(string UserId, string? UserName, string AccessToken);

/// <summary>One Kite mutual fund holding; a fund held in several folios comes once per folio.</summary>
/// <param name="Isin">Kite's tradingsymbol for mutual funds is the ISIN.</param>
public sealed record KiteMfHolding(string Isin, string Fund, string? Folio, decimal Quantity);

/// <summary>The Kite Connect v3 calls QuoteSnap needs: the login and the mutual fund holdings.</summary>
public sealed class KiteClient(HttpClient http, IOptions<ZerodhaOptions> options)
{
    private readonly ZerodhaOptions _options = options.Value;

    /// <summary>The Kite login page; Kite sends <paramref name="state"/> back to the app's redirect URL with the request token.</summary>
    public static string BuildLoginUrl(ZerodhaOptions options, string apiKey, string state) =>
        $"{options.LoginUrl}?v=3&api_key={Uri.EscapeDataString(apiKey)}&redirect_params={Uri.EscapeDataString("state=" + Uri.EscapeDataString(state))}";

    /// <summary>SHA-256 of api_key + request_token + api_secret, in lowercase hex.</summary>
    public static string Checksum(string apiKey, string requestToken, string apiSecret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey + requestToken + apiSecret)));

    public async Task<KiteLogin> ExchangeRequestTokenAsync(string apiKey, string apiSecret, string requestToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Url("session/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["api_key"] = apiKey,
                ["request_token"] = requestToken,
                ["checksum"] = Checksum(apiKey, requestToken, apiSecret),
            }),
        };
        using JsonDocument document = await SendAsync(request, cancellationToken);

        JsonElement data = document.RootElement.GetProperty("data");
        string userId = GetString(data, "user_id") ?? throw new KiteApiException("Kite did not return a user_id.");
        string accessToken = GetString(data, "access_token") ?? throw new KiteApiException("Kite did not return an access token.");
        return new KiteLogin(userId, GetString(data, "user_name"), accessToken);
    }

    public async Task<IReadOnlyList<KiteMfHolding>> GetMfHoldingsAsync(string apiKey, string accessToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, Url("mf/holdings"));
        request.Headers.Authorization = new AuthenticationHeaderValue("token", $"{apiKey}:{accessToken}");
        using JsonDocument document = await SendAsync(request, cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            throw new KiteApiException("Kite's mutual fund holdings response has no data list.");

        List<KiteMfHolding> holdings = [];
        foreach (JsonElement item in data.EnumerateArray())
        {
            string isin = GetString(item, "tradingsymbol") ?? throw new KiteApiException("A Kite mutual fund holding has no tradingsymbol.");
            decimal quantity = item.TryGetProperty("quantity", out JsonElement q) && q.ValueKind == JsonValueKind.Number ? q.GetDecimal() : 0;
            holdings.Add(new KiteMfHolding(isin.Trim().ToUpperInvariant(), GetString(item, "fund") ?? isin, GetString(item, "folio"), quantity));
        }

        return holdings;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Kite-Version", "3");
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
        }

        if (response.IsSuccessStatusCode && document is not null && GetString(document.RootElement, "status") == "success")
            return document;

        using (document)
        {
            string? message = document is null ? null : GetString(document.RootElement, "message");
            string? errorType = document is null ? null : GetString(document.RootElement, "error_type");
            throw KiteApiException.Create((int)response.StatusCode, errorType, message);
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String when value.GetString() is { Length: > 0 } s => s,
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private Uri Url(string path) => new(_options.ApiBaseUrl.TrimEnd('/') + "/" + path);
}

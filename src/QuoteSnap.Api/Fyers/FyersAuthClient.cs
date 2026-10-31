using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Fyers;

public sealed record FyersLoginTokens(string AccessToken, string RefreshToken);

public sealed record FyersProfile(string FyersId, string? Name);

public sealed class FyersAuthClient(HttpClient http, IOptions<FyersOptions> options)
{
    private readonly FyersOptions _options = options.Value;

    public static string BuildLoginUrl(FyersOptions options, string appId, string redirectUri, string state) =>
        $"{BaseUrl(options)}generate-authcode"
        + $"?client_id={Uri.EscapeDataString(appId)}"
        + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + "&response_type=code"
        + $"&state={Uri.EscapeDataString(state)}";

    public async Task<FyersLoginTokens> ExchangeAuthCodeAsync(
        FyersAppCredentials app,
        string authCode,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> body = new()
        {
            ["grant_type"] = "authorization_code",
            ["appIdHash"] = FyersTokens.AppIdHash(app.AppId, app.SecretId),
            ["code"] = authCode,
        };
        using HttpResponseMessage response = await http.PostAsJsonAsync(Url("validate-authcode"), body, cancellationToken);
        using JsonDocument document = await FyersJson.ReadOkAsync(response, cancellationToken);

        JsonElement root = document.RootElement;
        string accessToken = FyersJson.GetString(root, "access_token")
            ?? throw new FyersApiException("Fyers did not return an access token.");
        string refreshToken = FyersJson.GetString(root, "refresh_token")
            ?? throw new FyersApiException("Fyers did not return a refresh token.");
        return new FyersLoginTokens(accessToken, refreshToken);
    }

    public async Task<string> RefreshAccessTokenAsync(
        FyersAppCredentials app,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> body = new()
        {
            ["grant_type"] = "refresh_token",
            ["appIdHash"] = FyersTokens.AppIdHash(app.AppId, app.SecretId),
            ["refresh_token"] = refreshToken,
            ["pin"] = app.Pin,
        };
        using HttpResponseMessage response = await http.PostAsJsonAsync(Url("validate-refresh-token"), body, cancellationToken);
        using JsonDocument document = await FyersJson.ReadOkAsync(response, cancellationToken);

        return FyersJson.GetString(document.RootElement, "access_token")
            ?? throw new FyersApiException("Fyers did not return an access token.");
    }

    public async Task<FyersProfile> GetProfileAsync(string appId, string accessToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, Url("profile"));
        request.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{accessToken}");
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        using JsonDocument document = await FyersJson.ReadOkAsync(response, cancellationToken);

        JsonElement data = document.RootElement.TryGetProperty("data", out JsonElement d) ? d : default;
        string fyersId = FyersJson.GetString(data, "fy_id")
            ?? throw new FyersApiException("Fyers profile response did not include fy_id.");
        return new FyersProfile(fyersId, FyersJson.GetString(data, "name"));
    }

    /// <summary>Invalidates the access token for this app only; other apps and Fyers web and mobile sessions stay signed in.</summary>
    public async Task LogoutAsync(string appId, string accessToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Url("logout"));
        request.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{accessToken}");
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        (await FyersJson.ReadOkAsync(response, cancellationToken)).Dispose();
    }

    private Uri Url(string path) => new(BaseUrl(_options) + path);

    private static string BaseUrl(FyersOptions options) => options.ApiBaseUrl.TrimEnd('/') + "/";
}

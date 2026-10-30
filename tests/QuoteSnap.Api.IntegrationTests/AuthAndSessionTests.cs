using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class AuthAndSessionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key-0123456789")]
    [InlineData(QuoteSnapApiFactory.AdminApiKey)]
    public async Task Api_endpoints_require_the_api_key(string? key)
    {
        await using QuoteSnapApiFactory factory = new();
        using HttpClient http = factory.CreateClient();
        if (key is not null)
            http.DefaultRequestHeaders.Add(QuoteSnapHeaders.ApiKey, key);

        using HttpResponseMessage response = await http.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_endpoints_reject_the_regular_api_key()
    {
        await using QuoteSnapApiFactory factory = new();
        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(QuoteSnapHeaders.ApiKey, QuoteSnapApiFactory.ApiKey);

        using HttpResponseMessage response = await http.GetAsync("/admin/fyers/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_needs_no_key()
    {
        await using QuoteSnapApiFactory factory = new();
        using HttpClient http = factory.CreateClient();

        using HttpResponseMessage response = await http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Status_reports_relogin_required_without_a_session_and_ok_with_one()
    {
        await using QuoteSnapApiFactory factory = new();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        Assert.Equal(FyersAuthState.ReloginRequired, (await client.GetStatusAsync()).Fyers.State);

        await factory.SeedSessionAsync();
        FyersSessionInfo session = await client.GetFyersSessionAsync();

        Assert.Equal(FyersAuthState.Ok, session.State);
        Assert.Equal("XT0001", session.FyersId);
    }

    [Theory]
    [InlineData("", "test-secret", "0000")]
    [InlineData("TEST-100", "", "0000")]
    [InlineData("TEST-100", "test-secret", " ")]
    public async Task Login_requires_the_app_credentials(string appId, string secretId, string pin)
    {
        await using QuoteSnapApiFactory factory = new();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() =>
            factory.CreateQuoteSnapClient().SubmitFyersLoginAsync(new FyersAppCredentials(appId, secretId, pin), "auth-code"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/admin/fyers/login-url")]
    [InlineData("GET", "/admin/fyers/login-url?appId=TEST-100&redirectUri=http%3A%2F%2F127.0.0.1%3A8085%2Fcallback")]
    [InlineData("POST", "/admin/fyers/session")]
    public async Task Missing_admin_inputs_return_400(string method, string path)
    {
        await using QuoteSnapApiFactory factory = new();
        using HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(QuoteSnapHeaders.AdminKey, QuoteSnapApiFactory.AdminApiKey);
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_url_uses_the_given_app_and_redirect()
    {
        await using QuoteSnapApiFactory factory = new();

        FyersLoginUrl login = await factory.CreateQuoteSnapClient().GetFyersLoginUrlAsync(
            "TEST-100", "http://127.0.0.1:8085/callback", "0123456789abcdef0123456789abcdef");

        Assert.StartsWith("https://api-t1.fyers.in/api/v3/generate-authcode?client_id=TEST-100", login.Url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A8085%2Fcallback", login.Url);
        Assert.Contains("state=0123456789abcdef0123456789abcdef", login.Url);
    }

    [Fact]
    public async Task A_stale_refresh_cannot_overwrite_a_newer_login()
    {
        await using QuoteSnapApiFactory factory = new();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        SessionStore store = scope.ServiceProvider.GetRequiredService<SessionStore>();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        FyersAppCredentials app = QuoteSnapApiFactory.App;
        await store.SaveLoginAsync(new FyersSession("XT0001", app, "old-access", "old-refresh", now, now.AddDays(1), now, 0), default);
        FyersSession? beforeRefresh = await store.GetAsync(default);
        await store.SaveLoginAsync(new FyersSession("XT0001", app, "new-access", "new-refresh", now.AddHours(8), now.AddDays(15), now, 0), default);

        bool updated = await store.TryUpdateAccessTokenAsync(beforeRefresh!.Version, "stale-access", now.AddHours(8), default);

        Assert.False(updated);
        FyersSession? current = await store.GetAsync(default);
        Assert.Equal("new-access", current!.AccessToken);
        Assert.Equal("new-refresh", current.RefreshToken);
    }

    [Fact]
    public async Task Tokens_and_app_secrets_are_encrypted_at_rest_and_read_back()
    {
        await using QuoteSnapApiFactory factory = new();
        await factory.SeedSessionAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        QuoteSnapDbContext db = scope.ServiceProvider.GetRequiredService<QuoteSnapDbContext>();

        FyersSessionRecord record = db.FyersSessions.Single();
        FyersSession? session = await scope.ServiceProvider.GetRequiredService<SessionStore>().GetAsync(default);

        Assert.DoesNotContain("access-token", record.ProtectedAccessToken);
        Assert.DoesNotContain("refresh-token", record.ProtectedRefreshToken);
        Assert.DoesNotContain(QuoteSnapApiFactory.App.SecretId, record.ProtectedSecretId);
        Assert.NotEqual(QuoteSnapApiFactory.App.Pin, record.ProtectedPin);
        Assert.Equal(QuoteSnapApiFactory.App, session!.App);
    }
}

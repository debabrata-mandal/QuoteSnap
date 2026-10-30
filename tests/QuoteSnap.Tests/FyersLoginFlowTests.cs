using QuoteSnap.Client;

namespace QuoteSnap.Tests;

public sealed class FyersLoginFlowTests
{
    [Fact]
    public void ParseRedirect_returns_the_auth_code_when_state_matches()
    {
        Uri redirect = new("http://127.0.0.1:8085/callback?s=ok&code=200&auth_code=eyJ.abc.def&state=expected-state");

        Assert.Equal("eyJ.abc.def", FyersLoginFlow.ParseRedirect(redirect, "expected-state"));
    }

    [Fact]
    public void ParseRedirect_rejects_a_different_state()
    {
        Uri redirect = new("http://127.0.0.1:8085/callback?s=ok&auth_code=abc&state=someone-else");

        Assert.Throws<FyersLoginException>(() => FyersLoginFlow.ParseRedirect(redirect, "expected-state"));
    }

    [Fact]
    public void ParseRedirect_reports_fyers_errors()
    {
        Uri redirect = new("http://127.0.0.1:8085/callback?s=error&message=Invalid%20app&state=expected-state");

        FyersLoginException ex = Assert.Throws<FyersLoginException>(() => FyersLoginFlow.ParseRedirect(redirect, "expected-state"));
        Assert.Contains("Invalid app", ex.Message);
    }

    [Fact]
    public void ParseRedirect_requires_an_auth_code()
    {
        Uri redirect = new("http://127.0.0.1:8085/callback?s=ok&state=expected-state");

        Assert.Throws<FyersLoginException>(() => FyersLoginFlow.ParseRedirect(redirect, "expected-state"));
    }

    [Fact]
    public async Task Listener_ignores_other_paths_and_returns_the_callback_url()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start(0, "/callback");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Task<Uri> wait = listener.WaitForCallbackAsync(timeout.Token);
        using HttpClient http = new();

        HttpResponseMessage favicon = await http.GetAsync($"http://127.0.0.1:{listener.Port}/favicon.ico", timeout.Token);
        HttpResponseMessage callback = await http.GetAsync($"http://127.0.0.1:{listener.Port}/callback?auth_code=xyz&state=s1", timeout.Token);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, favicon.StatusCode);
        Assert.True(callback.IsSuccessStatusCode);
        Uri url = await wait;
        Assert.Equal("/callback", url.AbsolutePath);
        Assert.Equal("?auth_code=xyz&state=s1", url.Query);
    }
}

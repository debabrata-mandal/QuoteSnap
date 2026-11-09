using QuoteSnap.Client;

namespace QuoteSnap.Tests;

public sealed class ZerodhaMfSyncFlowTests
{
    [Fact]
    public void ParseRedirect_returns_the_request_token_when_state_matches()
    {
        Uri redirect = new("http://127.0.0.1:8085/zerodha/callback?action=login&type=login&status=success&request_token=tok123&state=expected-state");

        Assert.Equal("tok123", ZerodhaMfSyncFlow.ParseRedirect(redirect, "expected-state"));
    }

    [Fact]
    public void ParseRedirect_rejects_a_different_or_missing_state()
    {
        Assert.Throws<ZerodhaLoginException>(() => ZerodhaMfSyncFlow.ParseRedirect(new Uri("http://127.0.0.1:8085/zerodha/callback?status=success&request_token=tok&state=someone-else"), "expected-state"));
        Assert.Throws<ZerodhaLoginException>(() => ZerodhaMfSyncFlow.ParseRedirect(new Uri("http://127.0.0.1:8085/zerodha/callback?status=success&request_token=tok"), "expected-state"));
    }

    [Fact]
    public void ParseRedirect_reports_zerodha_errors()
    {
        Uri redirect = new("http://127.0.0.1:8085/zerodha/callback?status=error&message=Invalid%20api_key&state=expected-state");

        ZerodhaLoginException ex = Assert.Throws<ZerodhaLoginException>(() => ZerodhaMfSyncFlow.ParseRedirect(redirect, "expected-state"));
        Assert.Contains("Invalid api_key", ex.Message);
    }

    [Fact]
    public void ParseRedirect_requires_a_request_token()
    {
        Uri redirect = new("http://127.0.0.1:8085/zerodha/callback?status=success&state=expected-state");

        Assert.Throws<ZerodhaLoginException>(() => ZerodhaMfSyncFlow.ParseRedirect(redirect, "expected-state"));
    }
}

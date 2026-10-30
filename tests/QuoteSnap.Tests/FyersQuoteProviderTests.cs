using System.Net;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Fyers;

namespace QuoteSnap.Tests;

public sealed class FyersQuoteProviderTests
{
    private const string AppId = "XJ12345-100";

    private static readonly FyersOptions Options = new() { DataBaseUrl = "https://fyers.test/data/" };

    [Fact]
    public async Task Maps_quotes_errors_and_missing_symbols_in_request_order()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json("""
            {
              "s": "ok", "code": 200,
              "d": [
                { "n": "NSE:BAD-EQ", "s": "error", "v": { "errmsg": "invalid symbol" } },
                { "n": "NSE:SBIN-EQ", "s": "ok", "v": {
                    "lp": 812.55, "ch": -3.2, "chp": "-0.39", "open_price": 815, "high_price": 818.1,
                    "low_price": 809, "prev_close_price": 815.75, "volume": 12345678, "tt": "1761710400" } }
              ]
            }
            """));
        FyersQuoteProvider provider = CreateProvider(handler);

        IReadOnlyList<ProviderQuote> quotes = await provider.GetQuotesAsync(AppId, "token", ["NSE:SBIN-EQ", "NSE:BAD-EQ", "NSE:M&M-EQ"], CancellationToken.None);

        Assert.Equal(["NSE:SBIN-EQ", "NSE:BAD-EQ", "NSE:M&M-EQ"], quotes.Select(q => q.Symbol));

        ProviderQuote sbin = quotes[0];
        Assert.True(sbin.IsOk);
        Assert.Equal(812.55m, sbin.Ltp);
        Assert.Equal(-3.2m, sbin.Change);
        Assert.Equal(-0.39m, sbin.ChangePercent);
        Assert.Equal(815m, sbin.Open);
        Assert.Equal(815.75m, sbin.PreviousClose);
        Assert.Equal(12345678L, sbin.Volume);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1761710400), sbin.ExchangeTime);

        Assert.Equal("invalid symbol", quotes[1].Error);
        Assert.False(quotes[2].IsOk);
    }

    [Fact]
    public async Task Sends_the_fyers_authorization_header_and_escapes_symbols()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json("""{ "s": "ok", "d": [] }"""));

        await CreateProvider(handler).GetQuotesAsync(AppId, "abc", ["NSE:M&M-EQ", "NSE:SBIN-EQ"], CancellationToken.None);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("XJ12345-100:abc", request.Headers.GetValues("Authorization").Single());
        Assert.Equal(
            "https://fyers.test/data/quotes?symbols=NSE%3AM%26M-EQ,NSE%3ASBIN-EQ",
            request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Token_errors_raise_FyersAuthException()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json("""{ "s": "error", "code": -16, "message": "Could not authenticate" }"""));

        await Assert.ThrowsAsync<FyersAuthException>(() =>
            CreateProvider(handler).GetQuotesAsync(AppId, "expired", ["NSE:SBIN-EQ"], CancellationToken.None));
    }

    [Fact]
    public async Task Http_401_raises_FyersAuthException()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json("{}", HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<FyersAuthException>(() =>
            CreateProvider(handler).GetQuotesAsync(AppId, "expired", ["NSE:SBIN-EQ"], CancellationToken.None));
    }

    [Fact]
    public async Task Http_429_raises_FyersRateLimitException()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json("""{ "s": "error", "message": "limit" }""", HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<FyersRateLimitException>(() =>
            CreateProvider(handler).GetQuotesAsync(AppId, "token", ["NSE:SBIN-EQ"], CancellationToken.None));
    }

    private static FyersQuoteProvider CreateProvider(StubHttpHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options));
}

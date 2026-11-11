using Microsoft.Extensions.Time.Testing;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class MarketStatusCacheTests
{
    private readonly FakeTimeProvider _time = new();
    private int _reads;

    private Task<string?> ReadAsync(CancellationToken _)
    {
        _reads++;
        return Task.FromResult<string?>(_reads == 1 ? "OPEN" : "CLOSE");
    }

    [Fact]
    public async Task Callers_within_a_minute_share_one_fyers_read_and_later_callers_read_again()
    {
        MarketStatusCache cache = new(_time);
        MarketStatus first = await cache.GetAsync(ReadAsync, CancellationToken.None);
        _time.Advance(MarketStatusCache.Lifetime - TimeSpan.FromSeconds(1));
        Assert.Same(first, await cache.GetAsync(ReadAsync, CancellationToken.None));
        Assert.Equal(1, _reads);

        _time.Advance(TimeSpan.FromSeconds(1));
        MarketStatus second = await cache.GetAsync(ReadAsync, CancellationToken.None);
        Assert.Equal(2, _reads);
        Assert.False(second.IsOpen);
        Assert.Equal(_time.GetUtcNow(), second.CheckedAt);
    }

    [Fact]
    public async Task A_failed_read_is_not_kept()
    {
        MarketStatusCache cache = new(_time);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync(_ => throw new HttpRequestException("down"), CancellationToken.None));

        MarketStatus status = await cache.GetAsync(ReadAsync, CancellationToken.None);
        Assert.True(status.IsOpen);
        Assert.Equal(1, _reads);
    }
}

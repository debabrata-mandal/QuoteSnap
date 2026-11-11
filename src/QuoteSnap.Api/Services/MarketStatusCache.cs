using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>
/// Shares one Fyers market status read for a minute, so the desktop's title bar and automatic price fetching don't
/// each spend a Fyers call. Concurrent callers wait for the same read; failures aren't kept.
/// </summary>
public sealed class MarketStatusCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private MarketStatus? _last;

    public async Task<MarketStatus> GetAsync(Func<CancellationToken, Task<string?>> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_last is not null && time.GetUtcNow() - _last.CheckedAt < Lifetime)
                return _last;

            string? status = await read(cancellationToken);
            _last = new MarketStatus(status == "OPEN", status, time.GetUtcNow());
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }
}

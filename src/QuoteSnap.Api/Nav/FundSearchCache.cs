using System.Diagnostics;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Nav;

/// <summary>Holds the search index for the stored NAVs and rebuilds it after a refresh replaces them.</summary>
public sealed class FundSearchCache(ILogger<FundSearchCache> logger) : IDisposable
{
    private readonly SemaphoreSlim _rebuild = new(1, 1);
    private volatile Snapshot? _current;

    /// <returns>Null until NAVs have been downloaded.</returns>
    public async Task<Snapshot?> GetAsync(NavRepository repository, CancellationToken cancellationToken)
    {
        if (await repository.GetLoadedAtAsync(cancellationToken) is not { } loadedAt)
            return null;
        if (_current is { } current && current.LoadedAt == loadedAt)
            return current;

        await _rebuild.WaitAsync(cancellationToken);
        try
        {
            if (_current is { } rebuilt && rebuilt.LoadedAt == loadedAt)
                return rebuilt;

            long start = Stopwatch.GetTimestamp();
            List<MutualFundNav> navs = await repository.GetAllAsync(cancellationToken);
            if (navs.Count == 0)
                return null;

            Snapshot snapshot = new(FundSearchIndex.Build(navs), navs.Max(n => n.LoadedAt));
            _current = snapshot;
            logger.LogInformation(
                "Built the fund search index: {SchemeCount} schemes in {ElapsedMs:0} ms (NAVs loaded {LoadedAt})",
                snapshot.Index.Count, Stopwatch.GetElapsedTime(start).TotalMilliseconds, snapshot.LoadedAt);
            return snapshot;
        }
        finally
        {
            _rebuild.Release();
        }
    }

    public void Dispose() => _rebuild.Dispose();

    public sealed record Snapshot(FundSearchIndex Index, DateTimeOffset LoadedAt);
}

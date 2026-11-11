namespace QuoteSnap.Api.Services;

/// <summary>
/// Lets one POST /api/holdings/quotes at a time decide whether to reuse the latest request or submit a new one, so
/// clients asking at the same moment share a single Fyers fetch.
/// </summary>
public sealed class HoldingQuoteStartLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}

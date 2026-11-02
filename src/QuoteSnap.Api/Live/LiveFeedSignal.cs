using System.Threading.Channels;

namespace QuoteSnap.Api.Live;

/// <summary>Wakes <see cref="LiveFeedService"/> to rank the holdings again, e.g. after they're fetched or a fund is added or removed.</summary>
public sealed class LiveFeedSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <returns>True when notified; false when <paramref name="timeout"/> passed first.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timer = new(timeout, time);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        try
        {
            await _channel.Reader.ReadAsync(linked.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

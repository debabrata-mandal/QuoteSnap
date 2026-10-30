using System.Threading.Channels;

namespace QuoteSnap.Api.Services;

/// <summary>In-memory hand-off of holdings fetch IDs to the worker. The database remains the source of truth.</summary>
public sealed class HoldingFetchQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public void Enqueue(Guid fetchId) => _channel.Writer.TryWrite(fetchId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

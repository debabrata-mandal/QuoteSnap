using System.Threading.Channels;

namespace QuoteSnap.Api.Services;

/// <summary>In-memory hand-off of request IDs to the worker. SQLite remains the source of truth.</summary>
public sealed class QuoteRequestQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    public void Enqueue(Guid requestId) => _channel.Writer.TryWrite(requestId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

using Microsoft.AspNetCore.SignalR;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Events;

/// <summary>Only pushes; clients don't call it. The API key is checked before the connection is accepted.</summary>
public sealed class EventHub(HubPresence presence, ILogger<EventHub> logger) : Hub<IQuoteSnapEvents>
{
    public override Task OnConnectedAsync()
    {
        presence.Add();
        logger.LogInformation("Event hub client {ConnectionId} connected; {Clients} connected", Context.ConnectionId, presence.Count);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Remove();
        logger.LogInformation("Event hub client {ConnectionId} disconnected; {Clients} connected", Context.ConnectionId, presence.Count);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>How many clients are connected to <see cref="EventHub"/>, so nothing is computed for nobody.</summary>
public sealed class HubPresence
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Add() => Interlocked.Increment(ref _count);

    public void Remove() => Interlocked.Decrement(ref _count);
}

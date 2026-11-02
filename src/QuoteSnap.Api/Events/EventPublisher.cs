using Microsoft.AspNetCore.SignalR;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Events;

/// <summary>Pushes changes to every connected client. A failed push is logged and never fails the caller.</summary>
public sealed class EventPublisher(IHubContext<EventHub, IQuoteSnapEvents> hub, HubPresence presence, ILogger<EventPublisher> logger)
{
    private int _estimatesStale = 1;

    /// <summary>Whether something the NAV estimates depend on changed since the last call.</summary>
    public bool TakeEstimatesStale() => Interlocked.Exchange(ref _estimatesStale, 0) == 1;

    public Task QuoteRequestUpdatedAsync(QuoteRequest request)
    {
        if (request.ForHoldings && request.Status.IsTerminal())
            Interlocked.Exchange(ref _estimatesStale, 1);
        return SendAsync(nameof(IQuoteSnapEvents.QuoteRequestUpdated), () => $"{request.Id} {request.Status} {request.SucceededCount + request.FailedCount}/{request.TotalSymbols}", c => c.QuoteRequestUpdated(request.ToSummary()));
    }

    /// <summary>Reads the request again and pushes it.</summary>
    public async Task QuoteRequestUpdatedAsync(QuoteRequestRepository repository, Guid requestId, CancellationToken cancellationToken)
    {
        try
        {
            if (await repository.GetAsync(requestId, includeResults: false, cancellationToken) is { } request)
                await QuoteRequestUpdatedAsync(request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't read quote request {RequestId} to push it", requestId);
        }
    }

    /// <summary>Reads the fetch again and pushes it, and <see cref="ServerChange.Holdings"/> once it has finished.</summary>
    public async Task HoldingFetchUpdatedAsync(HoldingRepository repository, Guid fetchId, CancellationToken cancellationToken)
    {
        HoldingFetchRecord? fetch;
        try
        {
            fetch = await repository.GetFetchAsync(fetchId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't read holdings fetch {FetchId} to push it", fetchId);
            return;
        }

        if (fetch is null)
            return;
        HoldingFetch dto = fetch.ToDto();
        await SendAsync(nameof(IQuoteSnapEvents.HoldingFetchUpdated), () => $"{dto.Id} {dto.Status} {dto.FinishedFunds}/{dto.TotalFunds}", c => c.HoldingFetchUpdated(dto));
        if (fetch.Status.IsTerminal())
            await ChangedAsync(ServerChange.Holdings);
    }

    public Task ChangedAsync(ServerChange change)
    {
        if (change is ServerChange.MyFunds or ServerChange.Holdings or ServerChange.Navs)
            Interlocked.Exchange(ref _estimatesStale, 1);
        return SendAsync(nameof(IQuoteSnapEvents.Changed), () => change.ToString(), c => c.Changed(change));
    }

    public Task LiveFeedStatusChangedAsync(LiveFeedStatus status) => SendAsync(nameof(IQuoteSnapEvents.LiveFeedStatusChanged), () => $"{status.State}, {status.Symbols.Count} symbols, {status.QuoteCount} priced", c => c.LiveFeedStatusChanged(status));

    public Task LiveQuotesUpdatedAsync(LiveQuoteList quotes) => SendAsync(nameof(IQuoteSnapEvents.LiveQuotesUpdated), () => $"{quotes.Quotes.Count} quotes, last tick {quotes.LastTickAt?.ToLocalTime():HH:mm:ss}", c => c.LiveQuotesUpdated(quotes));

    public Task EstimatesUpdatedAsync(NavEstimateList estimates) => SendAsync(nameof(IQuoteSnapEvents.EstimatesUpdated), () => $"{estimates.Funds.Count(f => f.ChangePercent is not null)} of {estimates.Funds.Count} funds estimated, prices at {estimates.PricesAt?.ToLocalTime():HH:mm:ss}", c => c.EstimatesUpdated(estimates));

    private async Task SendAsync(string name, Func<string> describe, Func<IQuoteSnapEvents, Task> send)
    {
        try
        {
            await send(hub.Clients.All);
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Pushed {Event} to {Clients} client(s): {Detail}", name, presence.Count, describe());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't push {Event}", name);
        }
    }
}

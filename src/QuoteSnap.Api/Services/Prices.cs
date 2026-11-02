using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>A symbol's latest price and day change in percent, and when it was fetched or ticked.</summary>
public sealed record Price(decimal? Ltp, decimal? DayChange, DateTimeOffset? At);

/// <summary>Combines the last holdings quote request with the live feed: per symbol, whichever price is newer.</summary>
public static class Prices
{
    /// <param name="results">Results of the last finished holdings quote request; null when there's none.</param>
    /// <param name="live">The live feed's latest ticks.</param>
    public static Dictionary<string, Price> Merge(IEnumerable<QuoteResult>? results, IEnumerable<LiveQuote> live)
    {
        Dictionary<string, Price> prices = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuoteResult result in results ?? [])
        {
            if (result.Status == QuoteResultStatus.Ok)
                prices.TryAdd(result.Symbol, new Price(result.Ltp, NavEstimator.DayChange(result.ChangePercent, result.Change, result.Ltp), result.FetchedAt));
        }

        foreach (LiveQuote quote in live)
        {
            if (!prices.TryGetValue(quote.Symbol, out Price? fetched) || fetched.At is not { } fetchedAt || quote.UpdatedAt >= fetchedAt)
                prices[quote.Symbol] = new Price(quote.Ltp, NavEstimator.DayChange(quote.ChangePercent, quote.Change, quote.Ltp), quote.UpdatedAt);
        }

        return prices;
    }

    /// <summary>Each symbol's day change in percent, leaving out prices without one.</summary>
    public static Dictionary<string, decimal> DayChanges(IReadOnlyDictionary<string, Price> prices) =>
        prices.Where(p => p.Value.DayChange is not null).ToDictionary(p => p.Key, p => p.Value.DayChange!.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>When the newest of the prices arrived: the request's completion or the latest live tick used.</summary>
    public static DateTimeOffset? LatestAt(QuoteRequest? request, IReadOnlyDictionary<string, Price> prices)
    {
        DateTimeOffset? latest = request?.CompletedAt;
        foreach (Price price in prices.Values)
        {
            if (price.At > latest || latest is null)
                latest = price.At;
        }

        return latest;
    }
}

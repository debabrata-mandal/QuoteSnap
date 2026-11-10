using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>A symbol's latest price and day change in percent, and when it was fetched or ticked.</summary>
public sealed record Price(decimal? Ltp, decimal? DayChange, DateTimeOffset? At);

/// <summary>Combines the last holdings quote request with the live feed: per symbol, whichever price is newer.</summary>
public static class Prices
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <param name="results">Results of the last finished holdings quote request; null when there's none.</param>
    /// <param name="live">The live feed's latest ticks.</param>
    public static Dictionary<string, Price> Merge(IEnumerable<QuoteResult>? results, IEnumerable<LiveQuote> live)
    {
        Dictionary<string, Price> prices = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, QuoteResult> fetched = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuoteResult result in results ?? [])
        {
            if (result.Status == QuoteResultStatus.Ok && fetched.TryAdd(result.Symbol, result))
                prices[result.Symbol] = new Price(result.Ltp, NavEstimator.DayChange(result), result.FetchedAt);
        }

        foreach (LiveQuote quote in live)
        {
            if (!prices.TryGetValue(quote.Symbol, out Price? price) || price.At is not { } fetchedAt || quote.UpdatedAt >= fetchedAt)
                prices[quote.Symbol] = new Price(quote.Ltp, DayChange(quote, fetched.GetValueOrDefault(quote.Symbol)), quote.UpdatedAt);
        }

        return prices;
    }

    /// <summary>
    /// A tick's change against the fetched quote's previous close when that quote is from the tick's trading day, since
    /// a tick's own change can be the last session's (already in the last NAV) on a market holiday.
    /// </summary>
    private static decimal? DayChange(LiveQuote quote, QuoteResult? fetched)
    {
        if (fetched is { PreviousClose: { } previousClose, ExchangeTime: { } tradingDay } && previousClose > 0 && DateOnly.FromDateTime(tradingDay.UtcDateTime) == DateOnly.FromDateTime(quote.UpdatedAt.ToOffset(Ist).DateTime))
            return NavEstimator.Against(quote.Ltp, previousClose);
        return NavEstimator.DayChange(quote.ChangePercent, quote.Change, quote.Ltp);
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

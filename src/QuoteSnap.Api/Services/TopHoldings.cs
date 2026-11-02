using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>Ranks the stocks across the listed funds' latest holdings by their average weight.</summary>
public static class TopHoldings
{
    /// <param name="portfolios">Each fund's latest holdings, by scheme code.</param>
    /// <param name="quotes">Results of the last finished holdings quote request; null when there's none.</param>
    public static IReadOnlyList<TopHolding> Rank(IReadOnlyDictionary<int, List<FundHoldingRecord>> portfolios, IEnumerable<QuoteResult>? quotes, int limit)
    {
        if (portfolios.Count == 0)
            return [];

        Dictionary<string, QuoteResult> prices = quotes?.Where(q => q.Status == QuoteResultStatus.Ok).GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase) ?? [];
        return [.. portfolios.Values.SelectMany(h => h)
            .Where(h => h.Symbol is not null)
            .GroupBy(h => h.Symbol!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Symbol: g.Key, Lines: g.ToList(), Percent: g.Sum(h => h.Percent) / portfolios.Count))
            .Where(s => s.Percent > 0)
            .OrderByDescending(s => s.Percent)
            .ThenBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(s => ToDto(s.Symbol, s.Lines, s.Percent, prices.GetValueOrDefault(s.Symbol)))];
    }

    private static TopHolding ToDto(string symbol, List<FundHoldingRecord> lines, decimal percent, QuoteResult? quote)
    {
        FundHoldingRecord largest = lines.MaxBy(h => h.Percent)!;
        decimal? change = quote is null ? null : NavEstimator.DayChange(quote.ChangePercent, quote.Change, quote.Ltp);
        return new TopHolding(symbol, largest.Name, largest.Sector, Math.Round(percent, 4), lines.Select(h => h.SchemeCode).Distinct().Count(), quote?.Ltp, change is { } c ? Math.Round(c, 4) : null);
    }
}

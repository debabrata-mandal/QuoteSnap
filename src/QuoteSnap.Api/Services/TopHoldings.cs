using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>
/// Ranks the stocks across the listed funds' latest holdings by their average weight: weighted by the value held in
/// each fund when every fund has one, otherwise equally.
/// </summary>
public static class TopHoldings
{
    /// <summary>Each listed fund's value (units × latest NAV) by scheme code, or null unless every fund has units and a NAV.</summary>
    public static Dictionary<int, decimal>? Values(IReadOnlyCollection<MyFundRecord> funds, IEnumerable<MutualFundNav> navs)
    {
        Dictionary<int, decimal> latest = navs.ToDictionary(n => n.SchemeCode, n => n.Nav);
        Dictionary<int, decimal> values = [];
        foreach (MyFundRecord fund in funds)
        {
            if (fund.Units is not { } units || units <= 0 || !latest.TryGetValue(fund.SchemeCode, out decimal nav) || nav <= 0)
                return null;
            values[fund.SchemeCode] = units * nav;
        }

        return values.Count == 0 ? null : values;
    }

    public static async Task<Dictionary<int, decimal>?> LoadValuesAsync(MyFundRepository funds, NavRepository navs, CancellationToken cancellationToken)
    {
        List<MyFundRecord> saved = await funds.GetAllAsync(cancellationToken);
        return saved.Count == 0 ? null : Values(saved, await navs.FindBySchemeCodesAsync([.. saved.Select(f => f.SchemeCode)], cancellationToken));
    }

    /// <summary>Whether <paramref name="values"/> covers every fund in <paramref name="portfolios"/>, so the ranking weights by value.</summary>
    public static bool ByValue(IReadOnlyDictionary<int, List<FundHoldingRecord>> portfolios, IReadOnlyDictionary<int, decimal>? values) => values is not null && portfolios.Count > 0 && portfolios.Keys.All(values.ContainsKey);

    /// <param name="portfolios">Each fund's latest holdings, by scheme code.</param>
    /// <param name="values">Each fund's value from <see cref="Values"/>; null, or missing a fund, to weight the funds equally.</param>
    /// <param name="prices">Prices by symbol, from <see cref="Prices.Merge"/>; null to rank without prices.</param>
    public static IReadOnlyList<TopHolding> Rank(IReadOnlyDictionary<int, List<FundHoldingRecord>> portfolios, IReadOnlyDictionary<int, decimal>? values, IReadOnlyDictionary<string, Price>? prices, int limit)
    {
        if (portfolios.Count == 0)
            return [];

        bool byValue = ByValue(portfolios, values);
        decimal Weight(int schemeCode) => byValue ? values![schemeCode] : 1m;
        decimal total = portfolios.Keys.Sum(Weight);

        return [.. portfolios.Values.SelectMany(h => h)
            .Where(h => h.Symbol is not null)
            .GroupBy(h => h.Symbol!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Symbol: g.Key, Lines: g.ToList(), Percent: g.Sum(h => h.Percent * Weight(h.SchemeCode)) / total))
            .Where(s => s.Percent > 0)
            .OrderByDescending(s => s.Percent)
            .ThenBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(s => ToDto(s.Symbol, s.Lines, s.Percent, prices?.GetValueOrDefault(s.Symbol)))];
    }

    private static TopHolding ToDto(string symbol, List<FundHoldingRecord> lines, decimal percent, Price? price)
    {
        FundHoldingRecord largest = lines.MaxBy(h => h.Percent)!;
        return new TopHolding(symbol, largest.Name, largest.Sector, Math.Round(percent, 4), lines.Select(h => h.SchemeCode).Distinct().Count(), price?.Ltp, price?.DayChange is { } c ? Math.Round(c, 4) : null);
    }
}

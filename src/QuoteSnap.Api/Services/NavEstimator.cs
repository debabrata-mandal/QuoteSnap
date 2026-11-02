using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>Estimates how far a fund's NAV has moved today from its holdings' weights and their stocks' day changes.</summary>
public static class NavEstimator
{
    /// <summary>Each symbol's day change in percent, from the quotes that succeeded.</summary>
    public static Dictionary<string, decimal> DayChanges(IEnumerable<QuoteResult> results)
    {
        Dictionary<string, decimal> changes = new(StringComparer.OrdinalIgnoreCase);
        foreach (QuoteResult result in results)
        {
            if (result.Status == QuoteResultStatus.Ok && DayChange(result.ChangePercent, result.Change, result.Ltp) is { } change)
                changes[result.Symbol] = change;
        }

        return changes;
    }

    /// <summary>The quote's change percent, or else its change against the previous price (LTP minus change).</summary>
    public static decimal? DayChange(decimal? changePercent, decimal? change, decimal? ltp)
    {
        if (changePercent is { } percent)
            return percent;
        if (change is { } c && ltp is { } l && l - c != 0)
            return c / (l - c) * 100;
        return null;
    }

    /// <param name="holdings">The fund's holdings in one month.</param>
    /// <param name="dayChanges">Day change in percent by symbol; null when no prices have been fetched.</param>
    public static NavEstimate Estimate(int schemeCode, string schemeName, MutualFundNav? nav, IReadOnlyList<FundHoldingRecord> holdings, IReadOnlyDictionary<string, decimal>? dayChanges)
    {
        DateOnly? month = holdings.Count == 0 ? null : holdings.Max(h => h.Month);
        decimal change = 0;
        decimal pricedPercent = 0;
        int priced = 0;
        if (dayChanges is not null)
        {
            foreach (FundHoldingRecord holding in holdings)
            {
                if (holding.Symbol is not { } symbol || !dayChanges.TryGetValue(symbol, out decimal dayChange))
                    continue;
                change += holding.Percent * dayChange / 100;
                pricedPercent += holding.Percent;
                priced++;
            }
        }

        decimal? changePercent = priced == 0 ? null : Math.Round(change, 4);
        decimal? estimatedNav = changePercent is { } p && nav is not null ? Math.Round(nav.Nav * (1 + p / 100), 4) : null;
        return new NavEstimate(schemeCode, schemeName, nav?.Nav, nav?.NavDate, month, changePercent, estimatedNav, Math.Round(pricedPercent, 2), priced, holdings.Count);
    }
}

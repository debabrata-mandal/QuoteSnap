using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>
/// Everything today's NAV estimates need besides live prices: the listed funds, their NAVs and latest holdings, and the
/// last finished holdings quote request. Loaded once and reused for each second's live ticks.
/// </summary>
public sealed record EstimateBasis(List<MyFundRecord> Funds, Dictionary<int, MutualFundNav> Navs, Dictionary<int, List<FundHoldingRecord>> Portfolios, QuoteRequest? Request)
{
    public static async Task<EstimateBasis> LoadAsync(MyFundRepository funds, NavRepository navs, HoldingRepository holdings, QuoteRequestRepository requests, CancellationToken cancellationToken)
    {
        List<MyFundRecord> saved = await funds.GetAllAsync(cancellationToken);
        Dictionary<int, MutualFundNav> latest = saved.Count == 0 ? [] : (await navs.FindBySchemeCodesAsync([.. saved.Select(f => f.SchemeCode)], cancellationToken)).ToDictionary(n => n.SchemeCode);
        Dictionary<int, List<FundHoldingRecord>> portfolios = await holdings.GetLatestHoldingsAsync(cancellationToken);
        return new EstimateBasis(saved, latest, portfolios, await requests.GetLatestFinishedForHoldingsAsync(cancellationToken));
    }

    public static Task<EstimateBasis> LoadAsync(IServiceProvider services, CancellationToken cancellationToken) => LoadAsync(
        services.GetRequiredService<MyFundRepository>(),
        services.GetRequiredService<NavRepository>(),
        services.GetRequiredService<HoldingRepository>(),
        services.GetRequiredService<QuoteRequestRepository>(),
        cancellationToken);

    /// <param name="live">The live feed's latest ticks; per symbol, the newer of a tick and the request's price is used.</param>
    public NavEstimateList Estimate(IEnumerable<LiveQuote> live)
    {
        Dictionary<string, Price> prices = Prices.Merge(Request?.Results, live);
        Dictionary<string, decimal> dayChanges = Prices.DayChanges(prices);
        List<NavEstimate> estimates = [.. Funds.Select(f => Estimate(f, Navs.GetValueOrDefault(f.SchemeCode), Portfolios.GetValueOrDefault(f.SchemeCode) ?? [], dayChanges))];
        return new NavEstimateList(Request?.Id, Prices.LatestAt(Request, prices), estimates);

        static NavEstimate Estimate(MyFundRecord fund, MutualFundNav? nav, List<FundHoldingRecord> holdings, Dictionary<string, decimal> dayChanges) =>
            NavEstimator.Estimate(fund.SchemeCode, nav?.SchemeName ?? fund.SchemeName, nav, holdings, dayChanges);
    }
}

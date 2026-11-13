namespace QuoteSnap.Contracts;

/// <summary>A fund on the user's list.</summary>
/// <param name="SchemeName">As AMFI named it when the fund was added; <see cref="Latest"/> has the current name.</param>
/// <param name="Latest">The latest NAV; null when NAVs aren't loaded or AMFI's file no longer has the scheme.</param>
/// <param name="Holdings">The stored holdings; null until they've been fetched.</param>
/// <param name="Units">How many units the user holds; null until entered.</param>
public sealed record MyFund(int SchemeCode, string SchemeName, string? FundHouse, string? Option, string? Category, DateTimeOffset AddedAt, FundNav? Latest, HoldingsSummary? Holdings = null, decimal? Units = null);

/// <summary>How many units of a fund the user holds.</summary>
/// <param name="Units">Greater than 0, or null to clear.</param>
public sealed record SetFundUnits(decimal? Units);

/// <param name="Funds">In the user's order: the order they were added in until rearranged with PUT /api/my-funds/order.</param>
/// <param name="LoadedAt">When the AMFI file was last downloaded; null until the first refresh.</param>
public sealed record MyFundList(IReadOnlyList<MyFund> Funds, DateTimeOffset? LoadedAt);

/// <summary>A new order for the fund list.</summary>
/// <param name="SchemeCodes">Every fund on the list, each once, in the new order.</param>
public sealed record MyFundOrder(IReadOnlyList<int> SchemeCodes);

/// <summary>Today's NAV estimates for the funds on the list, from their holdings and the last live prices.</summary>
/// <param name="RequestId">The finished holdings quote request the prices come from; null when none has finished.</param>
/// <param name="PricesAt">When that request finished.</param>
/// <param name="Funds">In list order.</param>
public sealed record NavEstimateList(Guid? RequestId, DateTimeOffset? PricesAt, IReadOnlyList<NavEstimate> Funds);

/// <summary>A day's saved NAV estimates, from that day's last finished holdings quote request.</summary>
/// <param name="Day">The server's local date the prices were fetched on.</param>
/// <param name="PricesAt">When that request's prices were fetched.</param>
/// <param name="Funds">In the list order when saved; names are on GET /api/my-funds.</param>
public sealed record NavEstimateDay(DateOnly Day, DateTimeOffset? PricesAt, IReadOnlyList<SavedNavEstimate> Funds);

/// <summary>A fund's saved estimate; see <see cref="NavEstimate"/>.</summary>
public sealed record SavedNavEstimate(int SchemeCode, decimal? Nav, decimal? ChangePercent, decimal? EstimatedNav, int PricedHoldings);

/// <summary>
/// A fund's estimated move: each holding's weight times its stock's day change, summed. Holdings without a price
/// (cash, TREPS, bonds, failed quotes) count as unchanged.
/// </summary>
/// <param name="Nav">The latest published NAV the estimate builds on; null when AMFI's file doesn't have it.</param>
/// <param name="HoldingsMonth">The portfolio month used; null when holdings haven't been fetched.</param>
/// <param name="ChangePercent">The estimated change in percent; null without holdings or prices.</param>
/// <param name="EstimatedNav"><paramref name="Nav"/> moved by <paramref name="ChangePercent"/>.</param>
/// <param name="PricedPercent">The share of the fund's assets, in percent, whose holdings had a price.</param>
/// <param name="PricedHoldings">Holdings that had a price, out of <paramref name="TotalHoldings"/>.</param>
/// <param name="Units">How many units the user holds; null until entered.</param>
public sealed record NavEstimate(int SchemeCode, string SchemeName, decimal? Nav, DateOnly? NavDate, DateOnly? HoldingsMonth, decimal? ChangePercent, decimal? EstimatedNav, decimal PricedPercent, int PricedHoldings, int TotalHoldings, decimal? Units = null);

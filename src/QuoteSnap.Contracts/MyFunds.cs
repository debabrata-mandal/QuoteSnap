namespace QuoteSnap.Contracts;

/// <summary>A fund on the user's list.</summary>
/// <param name="SchemeName">As AMFI named it when the fund was added; <see cref="Latest"/> has the current name.</param>
/// <param name="Latest">The latest NAV; null when NAVs aren't loaded or AMFI's file no longer has the scheme.</param>
/// <param name="Holdings">The stored holdings; null until they've been fetched.</param>
public sealed record MyFund(
    int SchemeCode,
    string SchemeName,
    string? FundHouse,
    string? Option,
    string? Category,
    DateTimeOffset AddedAt,
    FundNav? Latest,
    HoldingsSummary? Holdings = null);

/// <param name="Funds">In the order they were added.</param>
/// <param name="LoadedAt">When the AMFI file was last downloaded; null until the first refresh.</param>
public sealed record MyFundList(IReadOnlyList<MyFund> Funds, DateTimeOffset? LoadedAt);

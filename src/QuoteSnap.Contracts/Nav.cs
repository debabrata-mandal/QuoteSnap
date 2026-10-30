namespace QuoteSnap.Contracts;

/// <param name="Isin">The growth or payout ISIN.</param>
/// <param name="Category">AMFI's category heading, e.g. "Open Ended Schemes(Equity Scheme - Large Cap Fund)".</param>
/// <param name="NavDate">Closed or matured schemes keep their last NAV, so this can be years old.</param>
public sealed record FundNav(
    int SchemeCode,
    string? Isin,
    string? IsinReinvestment,
    string SchemeName,
    string? FundHouse,
    string? Category,
    string? Plan,
    string? Option,
    decimal Nav,
    DateOnly NavDate);

/// <param name="Funds">In the order of the requested ISINs. An ISIN that belongs to several schemes returns all of them.</param>
/// <param name="LoadedAt">When the AMFI file was last downloaded.</param>
public sealed record NavLookup(IReadOnlyList<FundNav> Funds, IReadOnlyList<string> NotFound, DateTimeOffset LoadedAt);

public sealed record NavRefreshResult(int SchemeCount, int SkippedRows, DateOnly LatestNavDate, DateTimeOffset LoadedAt);

/// <param name="LoadedAt">When the AMFI file was last downloaded; null until the first refresh.</param>
public sealed record NavStatus(int SchemeCount, DateOnly? LatestNavDate, DateTimeOffset? LoadedAt);

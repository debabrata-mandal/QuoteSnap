namespace QuoteSnap.Contracts;

public sealed record ZerodhaLoginUrl(string Url);

/// <summary>The Kite Connect app (from developers.kite.trade). Used for one sync and never stored by the service.</summary>
public sealed record ZerodhaAppCredentials(string ApiKey, string ApiSecret);

/// <param name="RequestToken">From the redirect after the Kite login; it works once, within a few minutes.</param>
public sealed record SyncZerodhaMutualFunds(ZerodhaAppCredentials App, string RequestToken);

/// <summary>A fund the sync added, updated, removed or left alone.</summary>
/// <param name="Units">The units now held; for a removed fund, the units it had.</param>
public sealed record ZerodhaSyncedFund(int SchemeCode, string SchemeName, decimal? Units);

/// <summary>A Zerodha holding with no Direct Plan scheme in AMFI's NAV file, so it couldn't be put on the list.</summary>
/// <param name="Isin">Kite's tradingsymbol for the fund.</param>
public sealed record ZerodhaUnmatchedHolding(string Isin, string Fund, decimal Units);

/// <summary>
/// The fund list after making it match the Zerodha mutual fund holdings: held funds are added with their units, and
/// funds no longer held are removed.
/// </summary>
/// <param name="UserId">The Zerodha client ID that was logged in, e.g. "AB1234".</param>
/// <param name="Updated">Funds already on the list whose units changed.</param>
/// <param name="Unchanged">Funds already on the list with the same units.</param>
public sealed record ZerodhaMfSyncResult(string UserId, DateTimeOffset SyncedAt, IReadOnlyList<ZerodhaSyncedFund> Added, IReadOnlyList<ZerodhaSyncedFund> Updated, IReadOnlyList<ZerodhaSyncedFund> Removed, IReadOnlyList<ZerodhaSyncedFund> Unchanged, IReadOnlyList<ZerodhaUnmatchedHolding> Unmatched);

using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Events;
using QuoteSnap.Api.Live;
using QuoteSnap.Api.Zerodha;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

public sealed class ZerodhaAccountMismatchException(string message) : Exception(message);

/// <summary>
/// Syncs the fund list with the Zerodha mutual fund holdings. Every sync starts from a fresh Kite login, and its access
/// token is dropped once the holdings are read: nothing about the Zerodha login is stored. The token isn't revoked,
/// because that also signs the browser out of Kite web; Kite ends it at 6 AM IST.
/// </summary>
public sealed class ZerodhaService(
    KiteClient kite,
    MyFundRepository funds,
    NavRepository navs,
    LiveFeedSignal liveFeed,
    EventPublisher events,
    IOptions<ZerodhaOptions> options,
    TimeProvider time,
    ILogger<ZerodhaService> logger)
{
    private readonly ZerodhaOptions _options = options.Value;

    public string BuildLoginUrl(string apiKey, string state) => KiteClient.BuildLoginUrl(_options, apiKey, state);

    /// <summary>
    /// Makes the fund list match the Zerodha mutual fund holdings: adds held funds, sets their units (summed over
    /// folios) and removes funds no longer held. Holdings without a Direct Plan scheme in the NAV file are reported.
    /// </summary>
    /// <exception cref="KiteApiException">Kite rejected the login or failed.</exception>
    /// <exception cref="ZerodhaAccountMismatchException">The login was for another account than Zerodha:ExpectedUserId.</exception>
    public async Task<ZerodhaMfSyncResult> SyncMutualFundsAsync(ZerodhaAppCredentials app, string requestToken, CancellationToken cancellationToken)
    {
        KiteLogin login = await kite.ExchangeRequestTokenAsync(app.ApiKey, app.ApiSecret, requestToken, cancellationToken);
        if (_options.ExpectedUserId is { Length: > 0 } expected && !string.Equals(expected, login.UserId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Rejected a Zerodha login for {UserId}; only {ExpectedUserId} is allowed", login.UserId, expected);
            throw new ZerodhaAccountMismatchException($"This service only accepts Zerodha client ID {expected}, but the login was for {login.UserId}.");
        }

        IReadOnlyList<KiteMfHolding> holdings = await kite.GetMfHoldingsAsync(app.ApiKey, login.AccessToken, cancellationToken);

        // A fund held in several folios comes once per folio.
        List<(string Isin, string Fund, decimal Units)> held = [.. holdings
            .GroupBy(h => h.Isin)
            .Select(g => (Isin: g.Key, Fund: g.First().Fund, Units: decimal.Round(g.Sum(h => h.Quantity), 4)))
            .Where(h => h.Units > 0)];

        List<MutualFundNav> matches = held.Count == 0 ? [] : await navs.FindByIsinsAsync([.. held.Select(h => h.Isin)], cancellationToken);
        Dictionary<int, (MutualFundNav Nav, decimal Units)> wanted = [];
        List<ZerodhaUnmatchedHolding> unmatched = [];
        foreach ((string isin, string fund, decimal units) in held)
        {
            MutualFundNav? nav = matches.Where(n => n.Isin == isin || n.IsinReinvestment == isin).OrderBy(n => n.Isin == isin ? 0 : 1).ThenBy(n => n.SchemeCode).FirstOrDefault();
            if (nav is null)
            {
                unmatched.Add(new ZerodhaUnmatchedHolding(isin, fund, units));
                continue;
            }

            // A scheme's payout and reinvestment ISINs are both the same fund.
            wanted[nav.SchemeCode] = (nav, wanted.TryGetValue(nav.SchemeCode, out (MutualFundNav Nav, decimal Units) seen) ? seen.Units + units : units);
        }

        DateTimeOffset now = time.GetUtcNow();
        // PostgreSQL keeps microseconds; truncating makes the returned value match what later reads return.
        DateTimeOffset syncedAt = now.AddTicks(-(now.Ticks % 10));
        List<MyFundRecord> records = [.. wanted.Values.OrderBy(w => w.Nav.SchemeName, StringComparer.OrdinalIgnoreCase).Select(w => new MyFundRecord
        {
            SchemeCode = w.Nav.SchemeCode,
            SchemeName = w.Nav.SchemeName,
            FundHouse = w.Nav.FundHouse,
            Option = w.Nav.Option,
            Category = w.Nav.Category,
            AddedAt = syncedAt,
            Units = w.Units,
        })];

        MyFundSyncChanges changes = await funds.SyncAsync(records, cancellationToken);

        logger.LogInformation("Synced my funds with Zerodha {UserId}: {Added} added, {Updated} updated, {Removed} removed, {Unchanged} unchanged, {Unmatched} unmatched", login.UserId, changes.Added.Count, changes.Updated.Count, changes.Removed.Count, changes.Unchanged.Count, unmatched.Count);
        foreach (ZerodhaUnmatchedHolding holding in unmatched)
            logger.LogWarning("Zerodha holding {Isin} {Fund} has no Direct Plan scheme in the NAV file", holding.Isin, holding.Fund);

        if (changes.Added.Count + changes.Updated.Count + changes.Removed.Count > 0)
        {
            liveFeed.Notify();
            await events.ChangedAsync(ServerChange.MyFunds);
        }

        return new ZerodhaMfSyncResult(login.UserId, syncedAt, ToDto(changes.Added), ToDto(changes.Updated), ToDto(changes.Removed), ToDto(changes.Unchanged), unmatched);
    }

    private static List<ZerodhaSyncedFund> ToDto(List<MyFundRecord> records) => [.. records.Select(f => new ZerodhaSyncedFund(f.SchemeCode, f.SchemeName, f.Units))];
}

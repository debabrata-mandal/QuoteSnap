using System.Globalization;
using Polly;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Slack;

/// <summary>Why the NAV estimates can't be posted, and how to fix it.</summary>
/// <param name="Key">The kind of problem, so the 10-minute post doesn't post the same one again.</param>
public sealed record SlackProblem(string Key, string Text, string Fix)
{
    public const string CheckLogFix = "try again in a few minutes; if it keeps happening, check the API's log (`kubectl logs -n quotesnap deploy/quotesnap`).";

    public static readonly SlackProblem FyersLogin = new("fyers-login", "QuoteSnap needs a new Fyers login, so it can't check NSE or fetch prices.", "log in on the desktop app's Fyers login page, or run `quotesnap-login login`.");

    /// <summary>For Slack: the problem, then the fix on its own line.</summary>
    public string ToSlack() => $"⚠️ {SlackClient.Escape(Text)}\n*Fix:* {Fix}";

    /// <summary>For the desktop's Settings: one line.</summary>
    public override string ToString() => $"{Text} Fix: {Fix}";

    /// <summary>Slack refusing a post, or not answering, with what to do about it.</summary>
    public static SlackProblem FromSlack(Exception ex, string? channelId) => ex switch
    {
        SlackException { Error: "not_in_channel" or "channel_not_found" } s => new("slack-channel", $"Slack says the bot can't post in the channel ({s.Error}).", $"type `/invite @QuoteSnap` in <#{channelId}>."),
        SlackException { Error: "invalid_auth" or "not_authed" or "token_revoked" or "token_expired" or "account_inactive" } s => new("slack-token", $"Slack turned down the bot token ({s.Error}).", "reinstall the app (its Slack settings → Install App), put the new Bot User OAuth Token in `Slack__BotToken` and restart the API (`./k8s/deploy.ps1 -UpdateSecret`)."),
        SlackException { Error: "missing_scope" } s => new("slack-token", $"The bot is missing a permission ({s.Error}).", "add the `chat:write` bot scope under OAuth & Permissions, reinstall the app and update `Slack__BotToken`."),
        SlackException s => new("slack", $"Slack didn't take the post ({s.Error}).", CheckLogFix),
        _ => new("slack", $"Couldn't reach Slack: {ex.Message}", CheckLogFix),
    };
}

/// <summary>What to post, or why it can't be.</summary>
/// <param name="Market">NSE's status; null when Fyers couldn't say.</param>
/// <param name="FetchStarted">Whether a live-prices fetch was started for the next post.</param>
public sealed record SlackLatest(MarketStatus? Market, SlackMessage? Message, SlackProblem? Problem, bool FetchStarted = false);

/// <summary>
/// Decides what the Slack posts show. While NSE is trading: today's estimates, only if NAVs were downloaded today and the
/// estimates were saved in the last <see cref="MaxAge"/>, first starting a live-prices fetch when needed (see
/// <see cref="SlackPrices"/>). While it's closed: the latest saved day's estimates, marked as such.
/// </summary>
public static class SlackChecks
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    public static async Task<SlackLatest> PrepareAsync(IServiceProvider services, MarketStatusCache marketStatus, SessionService session, TimeProvider time, ILogger logger, CancellationToken cancellationToken)
    {
        MarketStatus market;
        try
        {
            IQuoteProvider provider = services.GetRequiredService<IQuoteProvider>();
            market = await marketStatus.GetAsync(ct => StatusEndpoints.ReadMarketStatusAsync(session, provider, ct), cancellationToken);
        }
        catch (Exception ex) when (ex is FyersReloginRequiredException or FyersAuthException)
        {
            return new SlackLatest(null, null, SlackProblem.FyersLogin);
        }
        catch (Exception ex) when (ex is FyersApiException or HttpRequestException or ExecutionRejectedException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Slack: couldn't read the market status from Fyers: {Error}", ex.Message);
            return new SlackLatest(null, null, new SlackProblem("market-status", $"Fyers couldn't say whether NSE is trading: {ex.Message}", SlackProblem.CheckLogFix));
        }

        List<MyFundRecord> funds = await services.GetRequiredService<MyFundRepository>().GetAllAsync(cancellationToken);
        if (funds.Count == 0)
            return new SlackLatest(market, null, new SlackProblem("no-funds", "There are no funds on your list.", "add them on the desktop app's Mutual funds page."));

        NavEstimateRepository estimates = services.GetRequiredService<NavEstimateRepository>();
        if (!market.IsOpen)
        {
            List<NavEstimateRecord> latest = await estimates.GetLatestDayAsync(cancellationToken);
            return latest.Count == 0
                ? new SlackLatest(market, null, new SlackProblem("no-estimates", $"NSE is closed and no NAV estimates were saved in the last {NavEstimateRepository.RetentionDays} days.", "they're saved after each live-prices fetch while NSE is trading; leave the desktop app open then, or try /latest during market hours."))
                : new SlackLatest(market, SlackReport.Build(funds, latest, marketClosed: true, time), null);
        }

        NavStatus navs = await services.GetRequiredService<NavRepository>().GetStatusAsync(cancellationToken);
        if (navs.LoadedAt is not { } loadedAt)
            return new SlackLatest(market, null, new SlackProblem("navs", "NAVs haven't been downloaded, so there's nothing to estimate from.", "refresh NAVs on the desktop app's Mutual funds page."));
        if (TimeZoneInfo.ConvertTime(loadedAt, time.LocalTimeZone).Date < time.GetLocalNow().Date)
            return new SlackLatest(market, null, new SlackProblem("navs", $"NAVs were last downloaded on {Day(loadedAt, time)}, so the estimated NAVs would be off.", "refresh NAVs on the desktop app's Mutual funds page."));

        QuoteRequestRepository requests = services.GetRequiredService<QuoteRequestRepository>();
        QuoteRequest? previous = await requests.GetLatestForHoldingsAsync(includeResults: false, cancellationToken);
        SlackFetch fetch = await SlackPrices.RefreshAsync(services, logger, cancellationToken);
        List<NavEstimateRecord> today = await estimates.GetTodayAsync(cancellationToken);
        if (today.Count > 0 && time.GetUtcNow() - today[0].SavedAt <= MaxAge)
            return new SlackLatest(market, SlackReport.Build(funds, today, marketClosed: false, time), null, fetch.Started);

        string saved = today.Count == 0 ? "No NAV estimates have been saved today yet" : $"The latest NAV estimates were saved at {Clock(today[0].SavedAt, time)}, over {MaxAge.TotalMinutes:0} minutes ago";
        if (fetch.Problem is { } cannotFetch)
            return new SlackLatest(market, null, cannotFetch with { Text = $"{saved}. {cannotFetch.Text}" }, false);

        bool running = fetch.Started || await requests.GetLatestForHoldingsAsync(includeResults: false, cancellationToken) is { } current && !current.Status.IsTerminal();
        string why = previous is { Status: QuoteRequestStatus.Failed } failed ? $" The last live-prices fetch failed: {failed.Error ?? "no reason given"}" : "";
        SlackProblem stale = running
            ? new SlackProblem("stale", $"{saved}.{why}", "a live-prices fetch is running; try /latest again in a minute.")
            : new SlackProblem("stale", $"{saved}.{why}", SlackProblem.CheckLogFix);
        return new SlackLatest(market, null, stale, fetch.Started);
    }

    private static string Clock(DateTimeOffset at, TimeProvider time) => TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset at, TimeProvider time) => TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).ToString("ddd, d MMM", CultureInfo.InvariantCulture);
}

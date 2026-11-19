using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Api.Events;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Slack;

/// <summary>
/// Before a Slack post, starts a live-prices fetch for the holdings unless one is running or started in the last
/// <see cref="FreshFor"/>, e.g. by the desktop app, so the desktop and Slack share one Fyers fetch. It doesn't wait for
/// it: the post uses the estimate saved so far and the next one gets the new prices.
/// </summary>
public static class SlackPrices
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);

    /// <returns>Whether a new fetch was started, or why none could be.</returns>
    public static async Task<SlackFetch> RefreshAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            Results<Accepted<QuoteRequestAccepted>, Ok<QuoteRequestAccepted>, ProblemHttpResult> started = await HoldingEndpoints.StartQuotesAsync((int)FreshFor.TotalSeconds, services.GetRequiredService<HoldingRepository>(), services.GetRequiredService<SymbolExclusionRepository>(), services.GetRequiredService<QuoteRequestRepository>(), services.GetRequiredService<QuoteRequestQueue>(), services.GetRequiredService<SessionService>(), services.GetRequiredService<EventPublisher>(), services.GetRequiredService<HoldingQuoteStartLock>(), services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILoggerFactory>(), cancellationToken);
            switch (started.Result)
            {
                case Accepted<QuoteRequestAccepted> accepted:
                    logger.LogInformation("Slack: no holdings prices in the last {Minutes} minutes, started quote request {RequestId}", FreshFor.TotalMinutes, accepted.Value!.RequestId);
                    return new SlackFetch(true, null);
                case ProblemHttpResult problem:
                    logger.LogWarning("Slack: couldn't start a holdings prices fetch: {Error}", problem.ProblemDetails.Detail);
                    return new SlackFetch(false, ToProblem(problem.ProblemDetails.Title, problem.ProblemDetails.Detail));
                default:
                    return new SlackFetch(false, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Slack: couldn't start a holdings prices fetch: {Error}", ex.Message);
            return new SlackFetch(false, new SlackProblem("fetch", $"Couldn't start a live-prices fetch: {ex.Message}", SlackProblem.CheckLogFix));
        }
    }

    private static SlackProblem ToProblem(string? title, string? detail) => title switch
    {
        "No holdings symbols" => new SlackProblem("holdings", "None of your funds has stock holdings with a Fyers symbol, so there are no prices to fetch.", "fetch the holdings on the desktop app's Holdings page."),
        "All symbols excluded" => new SlackProblem("holdings", "Every holdings symbol is excluded for today, so there are no prices to fetch.", "include some again on the desktop app's Holdings page."),
        "Fyers re-authentication required" => SlackProblem.FyersLogin,
        _ => new SlackProblem("fetch", $"Couldn't start a live-prices fetch: {detail ?? title}", SlackProblem.CheckLogFix),
    };
}

/// <param name="Started">Whether a new fetch was started; false when a recent or running one was reused, or none could start.</param>
/// <param name="Problem">Why no fetch could start.</param>
public sealed record SlackFetch(bool Started, SlackProblem? Problem);
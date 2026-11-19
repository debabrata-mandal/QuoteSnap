using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Polly;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Slack;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

/// <summary>Turning the Slack NAV estimate reports on and off, and trying them out.</summary>
public static class SlackEndpoints
{
    public const string NoEstimatesText = "QuoteSnap is connected. No NAV estimates have been saved today yet.";

    public static IEndpointRouteBuilder MapSlackEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/slack").RequireApiKey().WithTags("Slack");
        group.MapGet("/", GetAsync);
        group.MapPut("/", SetAsync);
        group.MapPost("/test", TestAsync);
        return app;
    }

    private static async Task<Ok<SlackStatus>> GetAsync(SlackSettingsRepository settings, IOptions<SlackOptions> options, SlackReporter reporter, CancellationToken cancellationToken) =>
        TypedResults.Ok(await StatusAsync(settings, options.Value, reporter, cancellationToken));

    private static async Task<Results<Ok<SlackStatus>, ProblemHttpResult>> SetAsync(SetSlackReports body, SlackSettingsRepository settings, IOptions<SlackOptions> options, SlackReporter reporter, CancellationToken cancellationToken)
    {
        if (body.Enabled && !options.Value.IsConfigured)
            return NotConfigured();

        await settings.SetEnabledAsync(body.Enabled, cancellationToken);
        return TypedResults.Ok(await StatusAsync(settings, options.Value, reporter, cancellationToken));
    }

    /// <summary>Posts today's saved estimates now, whether or not the reports are on or the market is open.</summary>
    private static async Task<Results<Ok<SlackTestResult>, ProblemHttpResult>> TestAsync(MyFundRepository funds, NavEstimateRepository estimates, SlackPoster slack, IOptions<SlackOptions> options, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
            return NotConfigured();

        SlackMessage message = await SlackReport.BuildAsync(funds, estimates, time, cancellationToken) ?? new SlackMessage(NoEstimatesText);
        try
        {
            await slack.PostAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is SlackException or HttpRequestException or ExecutionRejectedException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return TypedResults.Problem($"Slack didn't take the message: {ex.Message}", statusCode: StatusCodes.Status502BadGateway, title: "Slack failed");
        }

        return TypedResults.Ok(new SlackTestResult(message.Text));
    }

    private static async Task<SlackStatus> StatusAsync(SlackSettingsRepository settings, SlackOptions options, SlackReporter reporter, CancellationToken cancellationToken)
    {
        bool enabled = options.IsConfigured && await settings.GetEnabledAsync(cancellationToken);
        SlackReportResult? last = enabled ? reporter.LastReport : null;
        return new SlackStatus(options.IsConfigured, enabled, options.IntervalMinutes, reporter.NextReportAt, last?.At, last?.Result);
    }

    private static ProblemHttpResult NotConfigured() =>
        TypedResults.Problem("The API has no Slack bot. Set Slack__BotToken and Slack__ChannelId in its .env (or the Kubernetes secret) and restart it.", statusCode: StatusCodes.Status409Conflict, title: "Slack not set up");
}

using System.Globalization;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Slack;

/// <summary>
/// Every <see cref="SlackOptions.IntervalMinutes"/>, while the reports are turned on and Fyers says NSE is trading,
/// posts today's NAV estimates to Slack after <see cref="SlackChecks"/>, which also starts a live-prices fetch when
/// needed. When they can't be posted, the channel gets one post saying why and how to fix it.
/// </summary>
public sealed class SlackReporter(IOptions<SlackOptions> options, IServiceScopeFactory scopes, MarketStatusCache marketStatus, SessionService session, TimeProvider time, ILogger<SlackReporter> logger) : BackgroundService
{
    private volatile Schedule? _schedule;

    /// <summary>When the next report is due; null while the reporter isn't running.</summary>
    public DateTimeOffset? NextReportAt => _schedule?.NextAt;

    private volatile SlackReportResult? _lastReport;

    /// <summary>The last report while the reports were on: when it ran and what it did.</summary>
    public SlackReportResult? LastReport => _lastReport;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogInformation("Slack reports are off: Slack:BotToken (xoxb-...) and Slack:ChannelId aren't set.");
            return;
        }

        TimeSpan interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        using PeriodicTimer timer = new(interval, time);
        _schedule = new Schedule(time.GetUtcNow() + interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _schedule = new Schedule(time.GetUtcNow() + interval);
                await ReportAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _schedule = null;
        }
    }

    /// <summary>One report; public so tests can run it without waiting for the timer.</summary>
    /// <returns>Whether a message was posted.</returns>
    public async Task<bool> ReportAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IServiceProvider services = scope.ServiceProvider;
            if (!options.Value.IsConfigured || !await services.GetRequiredService<SlackSettingsRepository>().GetEnabledAsync(cancellationToken))
                return false;

            SlackLatest latest = await SlackChecks.PrepareAsync(services, marketStatus, session, time, logger, cancellationToken);
            if (latest.Market is { IsOpen: false } closed)
                return Record(false, $"Skipped at {Local()}: NSE isn't trading ({closed.Status ?? "no status from Fyers"}).");

            SlackPoster poster = services.GetRequiredService<SlackPoster>();
            string fetched = latest.FetchStarted ? " Started a live-prices fetch for the next post." : "";
            if (latest.Problem is { } problem)
            {
                logger.LogWarning("Slack: couldn't post the NAV estimates: {Problem}", problem);
                string shown = await poster.PostProblemAsync(problem, cancellationToken) ? " Posted the problem in the channel." : "";
                return Record(false, $"Couldn't post at {Local()}: {problem}{shown}{fetched}");
            }

            await poster.PostAsync(latest.Message!, cancellationToken);
            return Record(true, $"Posted at {Local()}.{fetched}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't post the NAV estimates to Slack: {Error}", ex.Message);
            return Record(false, $"Couldn't post at {Local()}: {(ex is SlackException or HttpRequestException ? SlackProblem.FromSlack(ex, options.Value.ChannelId) : ex.Message)}");
        }
    }

    private bool Record(bool posted, string result)
    {
        _lastReport = new SlackReportResult(time.GetUtcNow(), result);
        return posted;
    }

    private string Local() => time.GetLocalNow().ToString("HH:mm", CultureInfo.InvariantCulture);

    private sealed record Schedule(DateTimeOffset NextAt);
}

public sealed record SlackReportResult(DateTimeOffset At, string Result);

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
/// posts today's saved NAV estimates to Slack. Nothing is posted until an estimate has been saved today.
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

            IQuoteProvider provider = services.GetRequiredService<IQuoteProvider>();
            MarketStatus status = await marketStatus.GetAsync(ct => StatusEndpoints.ReadMarketStatusAsync(session, provider, ct), cancellationToken);
            if (!status.IsOpen)
                return Record(false, status.Error is null ? $"Skipped at {Local()}: NSE isn't trading ({status.Status ?? "no status from Fyers"})." : $"Skipped at {Local()}: couldn't read NSE's status ({status.Error}).");

            SlackMessage? message = await SlackReport.BuildAsync(services.GetRequiredService<MyFundRepository>(), services.GetRequiredService<NavEstimateRepository>(), time, cancellationToken);
            if (message is null)
                return Record(false, $"Skipped at {Local()}: no estimate saved today yet.");

            await services.GetRequiredService<SlackPoster>().PostAsync(message, cancellationToken);
            return Record(true, $"Posted at {Local()}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't post the NAV estimates to Slack: {Error}", ex.Message);
            return Record(false, $"Couldn't post at {Local()}: {ex.Message}");
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

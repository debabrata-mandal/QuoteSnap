using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Options;

namespace QuoteSnap.Api.Services;

/// <summary>Deletes quote requests and holdings fetch records older than the retention period, once at startup and then hourly.</summary>
public sealed class CleanupService(IServiceScopeFactory scopes, IOptions<QuoteSnapOptions> options, TimeProvider time, ILogger<CleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                DateTimeOffset cutoff = time.GetUtcNow().AddDays(-options.Value.RetentionDays);
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                QuoteRequestRepository repository = scope.ServiceProvider.GetRequiredService<QuoteRequestRepository>();
                int deleted = await repository.DeleteOlderThanAsync(cutoff, stoppingToken);
                int fetches = await scope.ServiceProvider.GetRequiredService<HoldingRepository>().DeleteFetchesOlderThanAsync(cutoff, stoppingToken);
                logger.LogInformation("Cleanup deleted {Requests} quote requests (with their results) and {Fetches} holdings fetch records older than {RetentionDays} days ({Cutoff}); next run in 1 hour", deleted, fetches, options.Value.RetentionDays, cutoff);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cleanup of old quote requests failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Options;

namespace QuoteSnap.Api.Services;

/// <summary>Deletes quote requests older than the retention period, once at startup and then daily.</summary>
public sealed class CleanupService(
    IServiceScopeFactory scopes,
    IOptions<QuoteSnapOptions> options,
    TimeProvider time,
    ILogger<CleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(24), time);
        do
        {
            try
            {
                DateTimeOffset cutoff = time.GetUtcNow().AddDays(-options.Value.RetentionDays);
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                QuoteRequestRepository repository = scope.ServiceProvider.GetRequiredService<QuoteRequestRepository>();
                int deleted = await repository.DeleteOlderThanAsync(cutoff, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} quote requests older than {Cutoff}", deleted, cutoff);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cleanup of old quote requests failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

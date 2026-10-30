using System.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Holdings;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Services;

/// <summary>
/// Runs holdings fetches one at a time: matches each listed fund to its ScanX scheme, downloads every page of its
/// past holdings and replaces the fund's stored holdings, recording progress per fund as it goes.
/// </summary>
public sealed class HoldingFetchWorker(
    HoldingFetchQueue queue,
    IServiceScopeFactory scopes,
    IOptions<HoldingsOptions> options,
    TimeProvider time,
    ILogger<HoldingFetchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueUnfinishedAsync(stoppingToken);

        await foreach (Guid fetchId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(fetchId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Holdings fetch {FetchId} failed unexpectedly", fetchId);
                await TryFailAsync(fetchId, $"Unexpected error: {ex.Message}", stoppingToken);
            }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        List<Guid> unfinished = await scope.ServiceProvider.GetRequiredService<HoldingRepository>().GetUnfinishedFetchIdsAsync(cancellationToken);
        if (unfinished.Count > 0)
            logger.LogInformation("Re-queueing {Count} unfinished holdings fetches", unfinished.Count);
        foreach (Guid id in unfinished)
            queue.Enqueue(id);
    }

    private async Task ProcessAsync(Guid fetchId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        HoldingRepository repository = scope.ServiceProvider.GetRequiredService<HoldingRepository>();
        MyFundRepository myFunds = scope.ServiceProvider.GetRequiredService<MyFundRepository>();
        ScanxClient scanx = scope.ServiceProvider.GetRequiredService<ScanxClient>();

        if (!await repository.TryStartAsync(fetchId, cancellationToken))
        {
            logger.LogDebug("Holdings fetch {FetchId} skipped: it has already finished or was deleted", fetchId);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        List<HoldingFetchFundRecord> funds = await repository.GetUnfinishedFundsAsync(fetchId, cancellationToken);
        logger.LogInformation("Holdings fetch {FetchId}: fetching holdings of {FundCount} funds from ScanX", fetchId, funds.Count);

        IReadOnlyList<ScanxScheme> schemes;
        try
        {
            schemes = await scanx.GetSchemesAsync(cancellationToken);
            logger.LogInformation("Holdings fetch {FetchId}: ScanX lists {SchemeCount} schemes", fetchId, schemes.Count);
        }
        catch (Exception ex) when (IsFetchFailure(ex, cancellationToken))
        {
            logger.LogWarning("Holdings fetch {FetchId}: couldn't load ScanX's scheme list. {ErrorType}: {Error}", fetchId, ex.GetType().Name, ex.Message);
            string error = $"Couldn't load ScanX's list of schemes: {ex.Message}";
            await repository.FailUnfinishedFundsAsync(fetchId, error, cancellationToken);
            await repository.CompleteAsync(fetchId, error, cancellationToken);
            return;
        }

        bool first = true;
        foreach (HoldingFetchFundRecord fund in funds)
        {
            await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.Running, null, null, null, null, cancellationToken);
            if (await myFunds.FindAsync(fund.SchemeCode, cancellationToken) is not { } listed)
            {
                await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.Error, null, null, null,
                    "Removed from the list before its holdings were fetched.", cancellationToken);
                continue;
            }

            if (ScanxSchemeMatcher.Match(listed.SchemeName, listed.FundHouse, schemes) is not { } scheme)
            {
                logger.LogWarning("Holdings fetch {FetchId}: no ScanX scheme matches {SchemeCode} {SchemeName} ({FundHouse})",
                    fetchId, listed.SchemeCode, listed.SchemeName, listed.FundHouse);
                await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.NotFound, null, null, null,
                    $"ScanX has no scheme named like “{listed.SchemeName}”.", cancellationToken);
                continue;
            }

            await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.Running, scheme.Name, null, null, null, cancellationToken);
            try
            {
                long fundStart = Stopwatch.GetTimestamp();
                List<ScanxHolding> holdings = await FetchAllPagesAsync(scanx, scheme, first, cancellationToken);
                first = false;
                List<FundHoldingRecord> rows = ToRecords(holdings);
                if (rows.Count == 0)
                {
                    await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.NotFound, scheme.Name, null, null,
                        "ScanX lists the scheme but has no holdings for it yet.", cancellationToken);
                    continue;
                }

                if (!await repository.ReplaceHoldingsAsync(fund.SchemeCode, scheme.PlanId, scheme.Name, rows, cancellationToken))
                {
                    await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.Error, scheme.Name, null, null,
                        "Removed from the list before its holdings were saved.", cancellationToken);
                    continue;
                }

                DateOnly month = rows.Max(r => r.Month);
                int count = rows.Count(r => r.Month == month);
                await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.Ok, scheme.Name, count, month, null, cancellationToken);
                logger.LogInformation(
                    "Holdings fetch {FetchId}: {SchemeCode} {SchemeName} → ScanX plan {PlanId} {ScanxScheme}: {Count} holdings in {Month:yyyy-MM}, {RowCount} rows over {MonthCount} months, in {ElapsedMs:0} ms",
                    fetchId, fund.SchemeCode, listed.SchemeName, scheme.PlanId, scheme.Name, count, month, rows.Count,
                    rows.Select(r => r.Month).Distinct().Count(), Stopwatch.GetElapsedTime(fundStart).TotalMilliseconds);
            }
            catch (Exception ex) when (IsFetchFailure(ex, cancellationToken))
            {
                first = false;
                logger.LogWarning("Holdings fetch {FetchId}: {SchemeCode} from ScanX plan {PlanId} failed. {ErrorType}: {Error}",
                    fetchId, fund.SchemeCode, scheme.PlanId, ex.GetType().Name, ex.Message);
                await repository.UpdateFundAsync(fetchId, fund.SchemeCode, HoldingFetchFundStatus.Error, scheme.Name, null, null,
                    $"Fetch failed: {ex.Message}", cancellationToken);
            }
        }

        HoldingFetchStatus status = await repository.CompleteAsync(fetchId, null, cancellationToken);
        logger.LogInformation("Holdings fetch {FetchId} finished as {Status} in {ElapsedMs:0} ms",
            fetchId, status, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private async Task<List<ScanxHolding>> FetchAllPagesAsync(ScanxClient scanx, ScanxScheme scheme, bool first, CancellationToken cancellationToken)
    {
        List<ScanxHolding> holdings = [];
        int totalPages = 1;
        for (int page = 1; page <= totalPages; page++)
        {
            if (!first || page > 1)
                await PauseAsync(cancellationToken);

            ScanxHoldingsPage result = await scanx.GetHoldingsPageAsync(scheme.PlanId, page, cancellationToken);
            holdings.AddRange(result.Holdings);
            totalPages = result.TotalPages;
            // Stops a page count that keeps growing, and an empty page past the end.
            if (result.Holdings.Count == 0 || page >= 20)
                break;
        }

        return holdings;
    }

    private Task PauseAsync(CancellationToken cancellationToken) => options.Value.DelayBetweenRequestsMs > 0
        ? Task.Delay(TimeSpan.FromMilliseconds(options.Value.DelayBetweenRequestsMs), time, cancellationToken)
        : Task.CompletedTask;

    /// <summary>A row per instrument and month it was held in; ScanX repeats every instrument for all twelve months.</summary>
    private static List<FundHoldingRecord> ToRecords(IEnumerable<ScanxHolding> holdings) =>
    [
        .. holdings.SelectMany(h => h.Months
            .Where(m => m.Percent != 0 || m.Shares != 0)
            .Select(m => new FundHoldingRecord
            {
                Month = m.Month,
                AssetType = Clip(h.AssetType, 100),
                Name = Clip(h.Name, 300),
                Symbol = h.Symbol is null ? null : Clip(h.Symbol, 100),
                Isin = h.Isin is { Length: 12 } isin ? isin : null,
                Sector = h.Sector is null ? null : Clip(h.Sector, 200),
                Percent = m.Percent,
                Shares = m.Shares == 0 ? null : m.Shares,
            })),
    ];

    private static string Clip(string value, int maxLength) => value.Length > maxLength ? value[..maxLength] : value;

    private static bool IsFetchFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or FormatException or ExecutionRejectedException
        || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private async Task TryFailAsync(Guid fetchId, string error, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            HoldingRepository repository = scope.ServiceProvider.GetRequiredService<HoldingRepository>();
            await repository.FailUnfinishedFundsAsync(fetchId, error, cancellationToken);
            await repository.CompleteAsync(fetchId, error, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not mark holdings fetch {FetchId} as failed", fetchId);
        }
    }
}

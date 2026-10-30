using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

public sealed class HoldingRepository(QuoteSnapDbContext db, TimeProvider time)
{
    /// <summary>A fetch of the funds' holdings, in list order.</summary>
    public async Task<HoldingFetchRecord> CreateFetchAsync(IReadOnlyList<MyFundRecord> funds, CancellationToken cancellationToken)
    {
        HoldingFetchRecord fetch = new()
        {
            Id = Guid.CreateVersion7(),
            Status = HoldingFetchStatus.Queued,
            CreatedAt = Truncate(time.GetUtcNow()),
        };
        for (int position = 0; position < funds.Count; position++)
        {
            fetch.Funds.Add(new HoldingFetchFundRecord
            {
                FetchId = fetch.Id,
                SchemeCode = funds[position].SchemeCode,
                Position = position,
                SchemeName = funds[position].SchemeName,
                Status = HoldingFetchFundStatus.Pending,
            });
        }

        db.HoldingFetches.Add(fetch);
        await db.SaveChangesAsync(cancellationToken);
        return fetch;
    }

    public Task<HoldingFetchRecord?> GetFetchAsync(Guid id, CancellationToken cancellationToken) =>
        db.HoldingFetches.AsNoTracking()
            .Include(f => f.Funds.OrderBy(x => x.Position))
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);

    public Task<HoldingFetchRecord?> GetLatestFetchAsync(CancellationToken cancellationToken) =>
        db.HoldingFetches.AsNoTracking()
            .Include(f => f.Funds.OrderBy(x => x.Position))
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Queued and running fetches, oldest first.</summary>
    public Task<List<Guid>> GetUnfinishedFetchIdsAsync(CancellationToken cancellationToken) =>
        db.HoldingFetches
            .Where(f => f.Status == HoldingFetchStatus.Queued || f.Status == HoldingFetchStatus.Running)
            .OrderBy(f => f.CreatedAt)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Moves a fetch to Running. Returns false if it doesn't exist or has already finished.</summary>
    public async Task<bool> TryStartAsync(Guid id, CancellationToken cancellationToken)
    {
        HoldingFetchRecord? fetch = await db.HoldingFetches.FindAsync([id], cancellationToken);
        if (fetch is null || fetch.Status.IsTerminal())
            return false;

        if (fetch.Status == HoldingFetchStatus.Queued)
        {
            fetch.Status = HoldingFetchStatus.Running;
            fetch.StartedAt = Truncate(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>Funds not yet finished, including one interrupted by a restart, in list order.</summary>
    public Task<List<HoldingFetchFundRecord>> GetUnfinishedFundsAsync(Guid id, CancellationToken cancellationToken) =>
        db.HoldingFetchFunds.AsNoTracking()
            .Where(f => f.FetchId == id && (f.Status == HoldingFetchFundStatus.Pending || f.Status == HoldingFetchFundStatus.Running))
            .OrderBy(f => f.Position)
            .ToListAsync(cancellationToken);

    public async Task UpdateFundAsync(
        Guid id,
        int schemeCode,
        HoldingFetchFundStatus status,
        string? scanxScheme,
        int? holdingCount,
        DateOnly? month,
        string? error,
        CancellationToken cancellationToken)
    {
        HoldingFetchFundRecord fund = await db.HoldingFetchFunds.FindAsync([id, schemeCode], cancellationToken)
            ?? throw new InvalidOperationException($"Holdings fetch {id} has no fund {schemeCode}.");
        fund.Status = status;
        fund.ScanxScheme = scanxScheme ?? fund.ScanxScheme;
        fund.HoldingCount = holdingCount;
        fund.Month = month;
        fund.Error = error is { Length: > 500 } ? error[..500] : error;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Marks every unfinished fund as failed, for a fetch that can't go on.</summary>
    public Task FailUnfinishedFundsAsync(Guid id, string error, CancellationToken cancellationToken) =>
        db.HoldingFetchFunds
            .Where(f => f.FetchId == id && (f.Status == HoldingFetchFundStatus.Pending || f.Status == HoldingFetchFundStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Status, HoldingFetchFundStatus.Error)
                .SetProperty(f => f.Error, error.Length > 500 ? error[..500] : error), cancellationToken);

    public async Task<HoldingFetchStatus> CompleteAsync(Guid id, string? error, CancellationToken cancellationToken)
    {
        HoldingFetchRecord fetch = await db.HoldingFetches.Include(f => f.Funds).SingleOrDefaultAsync(f => f.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Holdings fetch {id} no longer exists.");
        int ok = fetch.Funds.Count(f => f.Status == HoldingFetchFundStatus.Ok);
        fetch.Status = ok == fetch.Funds.Count ? HoldingFetchStatus.Completed
            : ok == 0 ? HoldingFetchStatus.Failed
            : HoldingFetchStatus.CompletedWithErrors;
        fetch.Error = error ?? (fetch.Status == HoldingFetchStatus.Failed ? "No fund's holdings could be fetched." : null);
        fetch.Error = fetch.Error is { Length: > 1000 } ? fetch.Error[..1000] : fetch.Error;
        fetch.CompletedAt = Truncate(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return fetch.Status;
    }

    /// <summary>Replaces a fund's stored holdings and records where they came from, in one transaction.</summary>
    /// <returns>False when the fund is no longer on the list.</returns>
    public async Task<bool> ReplaceHoldingsAsync(
        int schemeCode,
        int scanxPlanId,
        string scanxScheme,
        IReadOnlyList<FundHoldingRecord> holdings,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        MyFundRecord? fund = await db.MyFunds.SingleOrDefaultAsync(f => f.SchemeCode == schemeCode, cancellationToken);
        if (fund is null)
            return false;

        await db.FundHoldings.Where(h => h.SchemeCode == schemeCode).ExecuteDeleteAsync(cancellationToken);
        foreach (FundHoldingRecord holding in holdings)
            holding.SchemeCode = schemeCode;
        db.FundHoldings.AddRange(holdings);
        fund.ScanxPlanId = scanxPlanId;
        fund.ScanxScheme = scanxScheme;
        fund.HoldingsFetchedAt = Truncate(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    }

    /// <summary>The latest month and its holding count, for each of the funds that has holdings.</summary>
    public async Task<Dictionary<int, HoldingsSummary>> GetSummariesAsync(
        IReadOnlyList<MyFundRecord> funds,
        CancellationToken cancellationToken)
    {
        List<int> codes = [.. funds.Where(f => f.ScanxScheme is not null).Select(f => f.SchemeCode)];
        if (codes.Count == 0)
            return [];

        List<MonthCount> months = await db.FundHoldings
            .Where(h => codes.Contains(h.SchemeCode))
            .GroupBy(h => new { h.SchemeCode, h.Month })
            .Select(g => new MonthCount(g.Key.SchemeCode, g.Key.Month, g.Count()))
            .ToListAsync(cancellationToken);

        Dictionary<int, HoldingsSummary> summaries = [];
        foreach (MyFundRecord fund in funds)
        {
            if (fund.ScanxScheme is not { } scheme || fund.HoldingsFetchedAt is not { } fetchedAt)
                continue;
            if (months.Where(m => m.SchemeCode == fund.SchemeCode).MaxBy(m => m.Month) is { } latest)
                summaries[fund.SchemeCode] = new HoldingsSummary(scheme, latest.Month, latest.Count, fetchedAt);
        }

        return summaries;
    }

    private sealed record MonthCount(int SchemeCode, DateOnly Month, int Count);

    /// <summary>Months with stored holdings, newest first.</summary>
    public Task<List<DateOnly>> GetMonthsAsync(int schemeCode, CancellationToken cancellationToken) =>
        db.FundHoldings
            .Where(h => h.SchemeCode == schemeCode)
            .Select(h => h.Month)
            .Distinct()
            .OrderByDescending(m => m)
            .ToListAsync(cancellationToken);

    public Task<List<FundHoldingRecord>> GetHoldingsAsync(int schemeCode, DateOnly month, CancellationToken cancellationToken) =>
        db.FundHoldings.AsNoTracking()
            .Where(h => h.SchemeCode == schemeCode && h.Month == month)
            .OrderByDescending(h => h.Percent)
            .ThenBy(h => h.Name)
            .ToListAsync(cancellationToken);

    public async Task<int> DeleteFetchesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        await db.HoldingFetches.Where(f => f.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);

    // PostgreSQL keeps microseconds; truncating makes returned values match what later reads return.
    private static DateTimeOffset Truncate(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
}

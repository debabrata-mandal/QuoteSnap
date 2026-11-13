using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

/// <summary>The NAV estimates saved after each finished holdings quote request: one set per day, the day's last one winning.</summary>
public sealed class NavEstimateRepository(QuoteSnapDbContext db, TimeProvider time)
{
    public const int RetentionDays = 7;

    /// <summary>The server's local date.</summary>
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>Replaces today's saved estimates with <paramref name="estimates"/>.</summary>
    public async Task SaveTodayAsync(NavEstimateList estimates, CancellationToken cancellationToken)
    {
        if (estimates.RequestId is null)
            return;

        DateOnly today = Today;
        DateTimeOffset now = time.GetUtcNow();
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.NavEstimates.Where(e => e.Day == today).ExecuteDeleteAsync(cancellationToken);
        db.NavEstimates.AddRange(estimates.Funds.Select((f, i) => new NavEstimateRecord
        {
            Day = today,
            SchemeCode = f.SchemeCode,
            PricesAt = estimates.PricesAt,
            Nav = f.Nav,
            ChangePercent = f.ChangePercent,
            EstimatedNav = f.EstimatedNav,
            PricedHoldings = f.PricedHoldings,
            Position = i,
            SavedAt = now,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The saved days within the retention period, newest first, each in list order.</summary>
    public async Task<List<NavEstimateDay>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        DateOnly from = Today.AddDays(-(RetentionDays - 1));
        List<NavEstimateRecord> rows = await db.NavEstimates.AsNoTracking().Where(e => e.Day >= from).OrderByDescending(e => e.Day).ThenBy(e => e.Position).ToListAsync(cancellationToken);
        return [.. rows.GroupBy(e => e.Day).Select(g => new NavEstimateDay(g.Key, g.First().PricesAt, [.. g.Select(ToDto)]))];
    }

    /// <summary>Deletes days older than the retention period.</summary>
    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        DateOnly from = Today.AddDays(-(RetentionDays - 1));
        return db.NavEstimates.Where(e => e.Day < from).ExecuteDeleteAsync(cancellationToken);
    }

    private static SavedNavEstimate ToDto(NavEstimateRecord e) => new(e.SchemeCode, e.Nav, e.ChangePercent, e.EstimatedNav, e.PricedHoldings);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Data;

public sealed class NavRepository(QuoteSnapDbContext db)
{
    /// <summary>Replaces every stored NAV in one transaction, so readers see either the old file or the new one.</summary>
    public async Task ReplaceAllAsync(IReadOnlyList<MutualFundNav> navs, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serializes concurrent refreshes; EXCLUSIVE still allows reads.
        await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "MutualFundNavs" IN EXCLUSIVE MODE""", cancellationToken);
        await db.MutualFundNavs.ExecuteDeleteAsync(cancellationToken);
        db.MutualFundNavs.AddRange(navs);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public Task<DateTimeOffset?> GetLoadedAtAsync(CancellationToken cancellationToken) =>
        db.MutualFundNavs.MaxAsync(n => (DateTimeOffset?)n.LoadedAt, cancellationToken);

    /// <summary>Matches either ISIN column; an ISIN can belong to more than one scheme.</summary>
    public Task<List<MutualFundNav>> FindByIsinsAsync(IReadOnlyCollection<string> isins, CancellationToken cancellationToken) =>
        db.MutualFundNavs
            .AsNoTracking()
            .Where(n => isins.Contains(n.Isin!) || isins.Contains(n.IsinReinvestment!))
            .ToListAsync(cancellationToken);
}

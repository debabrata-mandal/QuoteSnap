using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace QuoteSnap.Api.Data;

public sealed class MyFundRepository(QuoteSnapDbContext db)
{
    /// <summary>In the order they were added.</summary>
    public Task<List<MyFundRecord>> GetAllAsync(CancellationToken cancellationToken) =>
        db.MyFunds.AsNoTracking().OrderBy(f => f.AddedAt).ThenBy(f => f.SchemeCode).ToListAsync(cancellationToken);

    public Task<MyFundRecord?> FindAsync(int schemeCode, CancellationToken cancellationToken) =>
        db.MyFunds.AsNoTracking().SingleOrDefaultAsync(f => f.SchemeCode == schemeCode, cancellationToken);

    /// <returns>The stored fund: <paramref name="fund"/>, or the existing row when the fund was already on the list.</returns>
    public async Task<(MyFundRecord Fund, bool Added)> AddAsync(MyFundRecord fund, CancellationToken cancellationToken)
    {
        if (await FindAsync(fund.SchemeCode, cancellationToken) is { } existing)
            return (existing, false);

        db.MyFunds.Add(fund);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (fund, true);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Added by a concurrent request.
            db.ChangeTracker.Clear();
            return (await FindAsync(fund.SchemeCode, cancellationToken) ?? fund, false);
        }
    }

    /// <returns>False when the fund wasn't on the list.</returns>
    public async Task<bool> RemoveAsync(int schemeCode, CancellationToken cancellationToken) =>
        await db.MyFunds.Where(f => f.SchemeCode == schemeCode).ExecuteDeleteAsync(cancellationToken) > 0;
}

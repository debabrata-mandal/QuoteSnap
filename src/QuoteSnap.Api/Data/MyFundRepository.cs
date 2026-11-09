using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace QuoteSnap.Api.Data;

public sealed record MyFundSyncChanges(List<MyFundRecord> Added, List<MyFundRecord> Updated, List<MyFundRecord> Removed, List<MyFundRecord> Unchanged);

public sealed class MyFundRepository(QuoteSnapDbContext db)
{
    /// <summary>In the user's order: the order they were added in until the user rearranges them.</summary>
    public Task<List<MyFundRecord>> GetAllAsync(CancellationToken cancellationToken) =>
        db.MyFunds.AsNoTracking().OrderBy(f => f.Position).ThenBy(f => f.AddedAt).ThenBy(f => f.SchemeCode).ToListAsync(cancellationToken);

    public Task<MyFundRecord?> FindAsync(int schemeCode, CancellationToken cancellationToken) =>
        db.MyFunds.AsNoTracking().SingleOrDefaultAsync(f => f.SchemeCode == schemeCode, cancellationToken);

    /// <returns>The stored fund: <paramref name="fund"/>, or the existing row when the fund was already on the list.</returns>
    public async Task<(MyFundRecord Fund, bool Added)> AddAsync(MyFundRecord fund, CancellationToken cancellationToken)
    {
        if (await FindAsync(fund.SchemeCode, cancellationToken) is { } existing)
            return (existing, false);

        fund.Position = (await db.MyFunds.MaxAsync(f => (int?)f.Position, cancellationToken) ?? -1) + 1;
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

    /// <summary>Rearranges the list.</summary>
    /// <param name="schemeCodes">Every fund on the list, each once, in the new order.</param>
    /// <returns>False, changing nothing, when <paramref name="schemeCodes"/> isn't exactly the funds on the list.</returns>
    public async Task<bool> ReorderAsync(IReadOnlyList<int> schemeCodes, CancellationToken cancellationToken)
    {
        Dictionary<int, MyFundRecord> funds = await db.MyFunds.ToDictionaryAsync(f => f.SchemeCode, cancellationToken);
        if (schemeCodes.Count != funds.Count || schemeCodes.Distinct().Count() != funds.Count || !schemeCodes.All(funds.ContainsKey))
            return false;

        for (int i = 0; i < schemeCodes.Count; i++)
            funds[schemeCodes[i]].Position = i;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <returns>The updated fund; null when it isn't on the list.</returns>
    public async Task<MyFundRecord?> SetUnitsAsync(int schemeCode, decimal? units, CancellationToken cancellationToken)
    {
        if (await db.MyFunds.SingleOrDefaultAsync(f => f.SchemeCode == schemeCode, cancellationToken) is not { } fund)
            return null;

        fund.Units = units;
        await db.SaveChangesAsync(cancellationToken);
        return fund;
    }

    /// <summary>
    /// Makes the list exactly <paramref name="wanted"/>, in one transaction: funds not on it yet are added last (in the
    /// given order), listed funds get the wanted units, and every other listed fund is removed with its holdings.
    /// </summary>
    public async Task<MyFundSyncChanges> SyncAsync(IReadOnlyList<MyFundRecord> wanted, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Dictionary<int, MyFundRecord> existing = await db.MyFunds.ToDictionaryAsync(f => f.SchemeCode, cancellationToken);
        HashSet<int> wantedCodes = [.. wanted.Select(f => f.SchemeCode)];

        MyFundSyncChanges changes = new([], [], [], []);
        foreach (MyFundRecord fund in existing.Values.Where(f => !wantedCodes.Contains(f.SchemeCode)).OrderBy(f => f.Position))
        {
            changes.Removed.Add(fund);
            db.MyFunds.Remove(fund);
        }

        int position = (existing.Values.Where(f => wantedCodes.Contains(f.SchemeCode)).Max(f => (int?)f.Position) ?? -1) + 1;
        foreach (MyFundRecord fund in wanted)
        {
            if (existing.TryGetValue(fund.SchemeCode, out MyFundRecord? listed))
            {
                if (listed.Units == fund.Units)
                {
                    changes.Unchanged.Add(listed);
                }
                else
                {
                    listed.Units = fund.Units;
                    changes.Updated.Add(listed);
                }
            }
            else
            {
                fund.Position = position++;
                db.MyFunds.Add(fund);
                changes.Added.Add(fund);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

    /// <returns>False when the fund wasn't on the list.</returns>
    public async Task<bool> RemoveAsync(int schemeCode, CancellationToken cancellationToken) =>
        await db.MyFunds.Where(f => f.SchemeCode == schemeCode).ExecuteDeleteAsync(cancellationToken) > 0;
}

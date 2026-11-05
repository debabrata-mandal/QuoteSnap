using Microsoft.EntityFrameworkCore;

namespace QuoteSnap.Api.Data;

/// <summary>The holdings symbols left out of today's live prices.</summary>
public sealed class SymbolExclusionRepository(QuoteSnapDbContext db, TimeProvider time)
{
    /// <summary>The server's local date, which exclusions last for.</summary>
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>Today's excluded symbols, sorted.</summary>
    public Task<List<ExcludedSymbolRecord>> GetTodayAsync(CancellationToken cancellationToken)
    {
        DateOnly today = Today;
        return db.ExcludedSymbols.AsNoTracking().Where(s => s.Day == today).OrderBy(s => s.Symbol).ToListAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetTodaySymbolsAsync(CancellationToken cancellationToken) =>
        new((await GetTodayAsync(cancellationToken)).Select(s => s.Symbol), StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes <paramref name="symbols"/> today's whole list; symbols already on it keep when they were excluded. Earlier days' rows are dropped.</summary>
    /// <returns>Whether the list changed.</returns>
    public async Task<bool> SetTodayAsync(IEnumerable<string> symbols, CancellationToken cancellationToken)
    {
        DateOnly today = Today;
        HashSet<string> wanted = new(symbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0));
        List<ExcludedSymbolRecord> stored = await db.ExcludedSymbols.ToListAsync(cancellationToken);
        List<ExcludedSymbolRecord> remove = [.. stored.Where(s => s.Day != today || !wanted.Contains(s.Symbol))];
        HashSet<string> kept = [.. stored.Where(s => s.Day == today && wanted.Contains(s.Symbol)).Select(s => s.Symbol)];
        List<string> add = [.. wanted.Where(s => !kept.Contains(s))];
        bool changed = add.Count > 0 || remove.Any(s => s.Day == today);
        if (remove.Count == 0 && add.Count == 0)
            return false;

        db.ExcludedSymbols.RemoveRange(remove);
        DateTimeOffset now = time.GetUtcNow();
        db.ExcludedSymbols.AddRange(add.Select(s => new ExcludedSymbolRecord { Symbol = s, Day = today, ExcludedAt = now }));
        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

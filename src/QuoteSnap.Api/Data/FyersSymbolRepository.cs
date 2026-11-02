using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

public sealed class FyersSymbolRepository(QuoteSnapDbContext db)
{
    /// <summary>Replaces every stored symbol in one transaction, so readers see either the old lists or the new ones.</summary>
    public async Task ReplaceAllAsync(IReadOnlyList<FyersSymbol> symbols, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serializes concurrent refreshes; EXCLUSIVE still allows reads.
        await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FyersSymbols" IN EXCLUSIVE MODE""", cancellationToken);
        await db.FyersSymbols.ExecuteDeleteAsync(cancellationToken);
        db.FyersSymbols.AddRange(symbols);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<FyersSymbolStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        await db.FyersSymbols
            .GroupBy(_ => 1)
            .Select(g => new FyersSymbolStatus(g.Count(), g.Max(s => (DateTimeOffset?)s.LoadedAt)))
            .SingleOrDefaultAsync(cancellationToken)
        ?? new FyersSymbolStatus(0, null);

    /// <summary>
    /// Stocks whose name, symbol or ISIN contains every word of <paramref name="query"/>, ignoring case. Exact tickers come first
    /// ("tatamotors" or "tata motors" finds NSE:TATAMOTORS-EQ), then tickers and names starting with the search, then shorter names.
    /// </summary>
    public async Task<List<FyersSymbol>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string compact = string.Concat(words).ToUpperInvariant();
        if (compact.Length == 0)
            return [];

        IQueryable<FyersSymbol> matches = db.FyersSymbols.AsNoTracking();
        foreach (string word in words)
        {
            string pattern = $"%{EscapeLike(word)}%";
            matches = matches.Where(s => EF.Functions.ILike(s.Name, pattern, LikeEscape)
                || EF.Functions.ILike(s.Symbol, pattern, LikeEscape)
                || EF.Functions.ILike(s.Isin, pattern, LikeEscape));
        }

        // Shortest names first, so broad words like "india" still reach the main companies; tickers are fetched separately.
        List<FyersSymbol> byName = await matches.OrderBy(s => s.Name.Length).ThenBy(s => s.Name).Take(CandidateLimit).ToListAsync(cancellationToken);
        string tickerPattern = $"%:{EscapeLike(compact)}%";
        List<FyersSymbol> byTicker = await db.FyersSymbols.AsNoTracking()
            .Where(s => EF.Functions.ILike(s.Symbol, tickerPattern, LikeEscape))
            .OrderBy(s => s.Symbol.Length)
            .Take(limit)
            .ToListAsync(cancellationToken);

        string upperQuery = string.Join(' ', words).ToUpperInvariant();
        return [.. byTicker.Concat(byName)
            .DistinctBy(s => s.Isin)
            .OrderBy(s => Rank(s, compact, upperQuery))
            .ThenBy(s => s.Name.Length)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Take(limit)];
    }

    private const int CandidateLimit = 300;
    private const string LikeEscape = "\\";

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static int Rank(FyersSymbol symbol, string compact, string upperQuery)
    {
        string ticker = Ticker(symbol.Symbol);
        if (ticker == compact || symbol.Isin == compact)
            return 0;
        if (ticker.StartsWith(compact, StringComparison.Ordinal))
            return 1;
        if (symbol.Name.ToUpperInvariant().StartsWith(upperQuery, StringComparison.Ordinal))
            return 2;
        return 3;
    }

    /// <summary>TATAMOTORS from NSE:TATAMOTORS-EQ, BAJAJ-AUTO from NSE:BAJAJ-AUTO-EQ.</summary>
    internal static string Ticker(string symbol)
    {
        int colon = symbol.IndexOf(':', StringComparison.Ordinal);
        int dash = symbol.LastIndexOf('-');
        int start = colon + 1;
        return (dash > start ? symbol[start..dash] : symbol[start..]).ToUpperInvariant();
    }

    /// <summary>The Fyers symbol of each of the ISINs that is listed.</summary>
    public async Task<Dictionary<string, string>> GetSymbolsByIsinAsync(IReadOnlyCollection<string> isins, CancellationToken cancellationToken) =>
        await db.FyersSymbols
            .AsNoTracking()
            .Where(s => isins.Contains(s.Isin))
            .ToDictionaryAsync(s => s.Isin, s => s.Symbol, StringComparer.Ordinal, cancellationToken);
}

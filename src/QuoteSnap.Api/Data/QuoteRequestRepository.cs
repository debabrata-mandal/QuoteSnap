using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

public sealed class QuoteRequestRepository(QuoteSnapDbContext db, TimeProvider time)
{
    public async Task<QuoteRequest> CreateAsync(IReadOnlyList<SymbolEntry> symbols, CancellationToken cancellationToken, bool forHoldings = false)
    {
        Guid id = Guid.CreateVersion7();
        QuoteRequest request = new()
        {
            Id = id,
            Status = QuoteRequestStatus.Queued,
            CreatedAt = time.GetUtcNow(),
            TotalSymbols = symbols.Count,
            FailedCount = symbols.Count(s => !s.IsValid),
            ForHoldings = forHoldings,
        };

        for (int position = 0; position < symbols.Count; position++)
        {
            SymbolEntry entry = symbols[position];
            request.Results.Add(new QuoteResult
            {
                RequestId = id,
                Symbol = entry.Symbol,
                Position = position,
                Status = entry.IsValid ? QuoteResultStatus.Pending : QuoteResultStatus.Invalid,
                Error = entry.IsValid ? null : SymbolValidator.InvalidSymbolMessage,
            });
        }

        db.QuoteRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    public Task<QuoteRequest?> GetAsync(Guid id, bool includeResults, CancellationToken cancellationToken)
    {
        IQueryable<QuoteRequest> query = db.QuoteRequests.AsNoTracking();
        if (includeResults)
            query = query.Include(r => r.Results.OrderBy(x => x.Position));
        return query.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    /// <summary>The most recent request for the holdings' symbols, from POST /api/holdings/quotes.</summary>
    public Task<QuoteRequest?> GetLatestForHoldingsAsync(bool includeResults, CancellationToken cancellationToken)
    {
        IQueryable<QuoteRequest> query = db.QuoteRequests.AsNoTracking().Where(r => r.ForHoldings);
        if (includeResults)
            query = query.Include(r => r.Results.OrderBy(x => x.Position));
        return query.OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Newest first. <paramref name="before"/> is an exclusive upper bound on CreatedAt.</summary>
    public Task<List<QuoteRequest>> ListAsync(int limit, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        IQueryable<QuoteRequest> query = db.QuoteRequests.AsNoTracking();
        if (before?.ToUniversalTime() is { } upperBound)
            query = query.Where(r => r.CreatedAt < upperBound);
        return query.OrderByDescending(r => r.CreatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public Task<List<Guid>> GetUnfinishedIdsAsync(CancellationToken cancellationToken) =>
        db.QuoteRequests
            .Where(r => r.Status == QuoteRequestStatus.Queued || r.Status == QuoteRequestStatus.Running)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Moves a request to Running. Returns false if it doesn't exist or has already finished.</summary>
    public async Task<bool> TryStartAsync(Guid id, CancellationToken cancellationToken)
    {
        QuoteRequest? request = await db.QuoteRequests.FindAsync([id], cancellationToken);
        if (request is null || request.Status.IsTerminal())
            return false;

        if (request.Status == QuoteRequestStatus.Queued)
        {
            request.Status = QuoteRequestStatus.Running;
            request.StartedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public Task<List<string>> GetPendingSymbolsAsync(Guid id, CancellationToken cancellationToken) =>
        db.QuoteResults
            .Where(r => r.RequestId == id && r.Status == QuoteResultStatus.Pending)
            .OrderBy(r => r.Position)
            .Select(r => r.Symbol)
            .ToListAsync(cancellationToken);

    /// <summary>Writes one batch of quotes and the updated counts in a single transaction.</summary>
    public async Task SaveBatchAsync(Guid id, IReadOnlyList<ProviderQuote> quotes, CancellationToken cancellationToken)
    {
        Dictionary<string, ProviderQuote> bySymbol = quotes.ToDictionary(q => q.Symbol, StringComparer.Ordinal);
        List<string> symbols = bySymbol.Keys.ToList();
        List<QuoteResult> rows = await db.QuoteResults
            .Where(r => r.RequestId == id && r.Status == QuoteResultStatus.Pending && symbols.Contains(r.Symbol))
            .ToListAsync(cancellationToken);

        DateTimeOffset fetchedAt = time.GetUtcNow();
        int succeeded = 0, failed = 0;
        foreach (QuoteResult row in rows)
        {
            ProviderQuote quote = bySymbol[row.Symbol];
            row.FetchedAt = fetchedAt;
            if (quote.IsOk)
            {
                row.Status = QuoteResultStatus.Ok;
                row.Ltp = quote.Ltp;
                row.Change = quote.Change;
                row.ChangePercent = quote.ChangePercent;
                row.Open = quote.Open;
                row.High = quote.High;
                row.Low = quote.Low;
                row.PreviousClose = quote.PreviousClose;
                row.Volume = quote.Volume;
                row.ExchangeTime = quote.ExchangeTime;
                succeeded++;
            }
            else
            {
                row.Status = QuoteResultStatus.Error;
                row.Error = Truncate(quote.Error, 500);
                failed++;
            }
        }

        QuoteRequest request = await db.QuoteRequests.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException($"Quote request {id} no longer exists.");
        request.SucceededCount += succeeded;
        request.FailedCount += failed;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailPendingAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        List<QuoteResult> rows = await db.QuoteResults
            .Where(r => r.RequestId == id && r.Status == QuoteResultStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (QuoteResult row in rows)
        {
            row.Status = QuoteResultStatus.Error;
            row.Error = Truncate(error, 500);
        }

        QuoteRequest request = await db.QuoteRequests.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException($"Quote request {id} no longer exists.");
        request.FailedCount += rows.Count;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<QuoteRequestStatus> CompleteAsync(Guid id, string? error, CancellationToken cancellationToken)
    {
        QuoteRequest request = await db.QuoteRequests.FindAsync([id], cancellationToken)
            ?? throw new InvalidOperationException($"Quote request {id} no longer exists.");

        request.Status = (request.SucceededCount, request.FailedCount) switch
        {
            (_, 0) => QuoteRequestStatus.Completed,
            (0, _) => QuoteRequestStatus.Failed,
            _ => QuoteRequestStatus.CompletedWithErrors,
        };
        request.Error = Truncate(
            error ?? (request.Status == QuoteRequestStatus.Failed ? "No quotes could be fetched." : null),
            1000);
        request.CompletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return request.Status;
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.QuoteResults
            .Where(x => db.QuoteRequests.Any(r => r.Id == x.RequestId && r.CreatedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);
        int deleted = await db.QuoteRequests
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}

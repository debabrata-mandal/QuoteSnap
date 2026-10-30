using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Domain;

public sealed class QuoteRequest
{
    public Guid Id { get; set; }

    public QuoteRequestStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int TotalSymbols { get; set; }

    public int SucceededCount { get; set; }

    public int FailedCount { get; set; }

    public string? Error { get; set; }

    public List<QuoteResult> Results { get; set; } = [];
}

public sealed class QuoteResult
{
    public Guid RequestId { get; set; }

    public required string Symbol { get; set; }

    /// <summary>The symbol's position in the caller's original list.</summary>
    public int Position { get; set; }

    public QuoteResultStatus Status { get; set; }

    public decimal? Ltp { get; set; }

    public decimal? Change { get; set; }

    public decimal? ChangePercent { get; set; }

    public decimal? Open { get; set; }

    public decimal? High { get; set; }

    public decimal? Low { get; set; }

    public decimal? PreviousClose { get; set; }

    public long? Volume { get; set; }

    public DateTimeOffset? ExchangeTime { get; set; }

    public DateTimeOffset? FetchedAt { get; set; }

    public string? Error { get; set; }
}

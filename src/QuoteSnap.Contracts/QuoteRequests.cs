namespace QuoteSnap.Contracts;

public sealed record CreateQuoteRequest(IReadOnlyList<string> Symbols);

public sealed record QuoteRequestAccepted(
    Guid RequestId,
    QuoteRequestStatus Status,
    int TotalSymbols,
    IReadOnlyList<string> InvalidSymbols,
    string StatusUrl,
    int? RefreshTokenExpiresInDays);

public sealed record QuoteRequestSummary(
    Guid RequestId,
    QuoteRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalSymbols,
    int SucceededCount,
    int FailedCount,
    string? Error);

public sealed record QuoteRequestDetails(
    Guid RequestId,
    QuoteRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalSymbols,
    int SucceededCount,
    int FailedCount,
    string? Error,
    IReadOnlyList<QuoteResultDto> Results);

public sealed record QuoteResultDto(
    string Symbol,
    QuoteResultStatus Status,
    decimal? Ltp,
    decimal? Change,
    decimal? ChangePercent,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? PreviousClose,
    long? Volume,
    DateTimeOffset? ExchangeTime,
    DateTimeOffset? FetchedAt,
    string? Error);

/// <param name="NextBefore">Pass as <c>before</c> to fetch the next (older) page; null when there are no more.</param>
public sealed record QuoteRequestPage(IReadOnlyList<QuoteRequestSummary> Items, DateTimeOffset? NextBefore);

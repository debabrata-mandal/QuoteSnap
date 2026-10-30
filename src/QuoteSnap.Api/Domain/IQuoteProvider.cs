namespace QuoteSnap.Api.Domain;

public interface IQuoteProvider
{
    /// <summary>Returns exactly one quote per requested symbol, in the same order.</summary>
    Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(
        string appId,
        string accessToken,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken);
}

public sealed record ProviderQuote(string Symbol)
{
    public decimal? Ltp { get; init; }

    public decimal? Change { get; init; }

    public decimal? ChangePercent { get; init; }

    public decimal? Open { get; init; }

    public decimal? High { get; init; }

    public decimal? Low { get; init; }

    public decimal? PreviousClose { get; init; }

    public long? Volume { get; init; }

    public DateTimeOffset? ExchangeTime { get; init; }

    public string? Error { get; init; }

    public bool IsOk => Error is null;

    public static ProviderQuote Failure(string symbol, string error) => new(symbol) { Error = error };
}

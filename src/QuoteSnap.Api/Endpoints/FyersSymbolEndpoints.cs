using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Holdings;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

/// <summary>The stored copy of Fyers' symbol masters, which gives fund holdings their Fyers symbols.</summary>
public static class FyersSymbolEndpoints
{
    public static IEndpointRouteBuilder MapFyersSymbolEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/symbols").RequireApiKey().WithTags("Fyers symbols");
        group.MapGet("/status", (FyersSymbolRepository repository, CancellationToken cancellationToken) =>
            repository.GetStatusAsync(cancellationToken));
        group.MapGet("/search", SearchAsync);
        group.MapPost("/refresh", RefreshAsync);
        return app;
    }

    private const int DefaultSearchLimit = 20;
    private const int MaxSearchLimit = 50;

    /// <param name="q">Part of a company name, a ticker or an ISIN: "tata motors", "hdfcbank", "INE155A01022".</param>
    /// <param name="limit">At most 50; 20 by default.</param>
    private static async Task<Results<Ok<FyersSymbolSearchResult>, ProblemHttpResult>> SearchAsync(string? q, int? limit, FyersSymbolRepository repository, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        string query = (q ?? "").Trim();
        if (query.Length < 2)
            return TypedResults.Problem("Provide a search of at least 2 characters in 'q'.", statusCode: StatusCodes.Status400BadRequest);

        if ((await repository.GetStatusAsync(cancellationToken)).LoadedAt is not { } loadedAt)
            return NotLoaded();

        List<FyersSymbol> found = await repository.SearchAsync(query, Math.Clamp(limit ?? DefaultSearchLimit, 1, MaxSearchLimit), cancellationToken);
        loggers.CreateLogger(typeof(FyersSymbolEndpoints)).LogInformation("Symbol search for {Query}: {SymbolCount} stocks", query, found.Count);
        return TypedResults.Ok(new FyersSymbolSearchResult(query, [.. found.Select(s => new FyersSymbolMatch(s.Symbol, s.Name, s.Isin))], loadedAt));
    }

    internal static ProblemHttpResult NotLoaded() => TypedResults.Problem("Fyers symbols haven't been downloaded yet. Call POST /api/symbols/refresh first.", statusCode: StatusCodes.Status503ServiceUnavailable, title: "Symbols not loaded");

    /// <summary>Downloads NSE_CM.csv and BSE_CM.csv again and replaces the stored symbols.</summary>
    private static async Task<Results<Ok<FyersSymbolRefreshResult>, ProblemHttpResult>> RefreshAsync(FyersSymbolImporter importer, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await importer.RefreshAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            loggers.CreateLogger(typeof(FyersSymbolEndpoints)).LogWarning(
                "Fyers symbol refresh failed; the stored symbols are unchanged. {ErrorType}: {Error}", ex.GetType().Name, ex.Message);
            return TypedResults.Problem($"Could not load the Fyers symbol masters; the stored symbols are unchanged. {ex.Message}", statusCode: StatusCodes.Status502BadGateway, title: "Symbol download failed");
        }
    }
}

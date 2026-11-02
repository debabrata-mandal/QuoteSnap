using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Events;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

public static class QuoteRequestEndpoints
{
    private const string RetryAfterSeconds = "2";

    public static IEndpointRouteBuilder MapQuoteRequestEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/quote-requests").RequireApiKey().WithTags("Quote requests");
        group.MapPost("/", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/", ListAsync);
        return app;
    }

    private static Task<Results<Accepted<QuoteRequestAccepted>, ProblemHttpResult>> CreateAsync(CreateQuoteRequest body, QuoteRequestRepository repository, QuoteRequestQueue queue, SessionService session, EventPublisher events, TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken) =>
        SubmitAsync(body.Symbols, forHoldings: false, repository, queue, session, events, time, loggers, cancellationToken);

    /// <summary>Validates the symbols, checks the Fyers login, then stores and queues the request.</summary>
    internal static async Task<Results<Accepted<QuoteRequestAccepted>, ProblemHttpResult>> SubmitAsync(IReadOnlyList<string>? symbols, bool forHoldings, QuoteRequestRepository repository, QuoteRequestQueue queue, SessionService session, EventPublisher events, TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ILogger logger = loggers.CreateLogger(typeof(QuoteRequestEndpoints));
        if (symbols is not { Count: > 0 })
            return TypedResults.Problem("Provide at least one symbol in 'symbols'.", statusCode: StatusCodes.Status400BadRequest);

        SymbolValidationResult validation = SymbolValidator.Validate(symbols);
        if (validation.Valid.Count == 0)
        {
            return TypedResults.Problem("None of the symbols are valid. Use the EXCHANGE:SYMBOL-SERIES format, for example NSE:SBIN-EQ.", statusCode: StatusCodes.Status400BadRequest, extensions: new Dictionary<string, object?> { ["invalidSymbols"] = validation.Invalid });
        }

        FyersSessionInfo fyers = await session.GetInfoAsync(cancellationToken);
        if (fyers.State == FyersAuthState.ReloginRequired)
        {
            logger.LogWarning("Rejected a quote request for {SymbolCount} symbols: Fyers needs a new login", validation.Valid.Count);
            return TypedResults.Problem(FyersReloginRequiredException.DefaultMessage, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Fyers re-authentication required");
        }

        QuoteRequest request = await repository.CreateAsync(validation.Entries, cancellationToken, forHoldings);
        queue.Enqueue(request.Id);
        await events.QuoteRequestUpdatedAsync(request);
        logger.LogInformation("Queued quote request {RequestId}: {SymbolCount} symbols, {InvalidCount} invalid, for holdings: {ForHoldings}", request.Id, request.TotalSymbols, validation.Invalid.Count, forHoldings);

        string statusUrl = $"/api/quote-requests/{request.Id}";
        int? refreshDays = fyers.RefreshTokenExpiresAt is { } expiresAt
            ? Math.Max(0, (int)(expiresAt - time.GetUtcNow()).TotalDays)
            : null;

        return TypedResults.Accepted(
            statusUrl,
            new QuoteRequestAccepted(request.Id, request.Status, request.TotalSymbols, validation.Invalid, statusUrl, refreshDays));
    }

    private static async Task<Results<Ok<QuoteRequestDetails>, ProblemHttpResult>> GetAsync(Guid id, QuoteRequestRepository repository, HttpContext http, CancellationToken cancellationToken)
    {
        QuoteRequest? request = await repository.GetAsync(id, includeResults: true, cancellationToken);
        if (request is null)
            return TypedResults.Problem($"Quote request {id} was not found.", statusCode: StatusCodes.Status404NotFound);

        if (!request.Status.IsTerminal())
            http.Response.Headers.RetryAfter = RetryAfterSeconds;

        return TypedResults.Ok(request.ToDetails());
    }

    private static async Task<Ok<QuoteRequestPage>> ListAsync(QuoteRequestRepository repository, int? limit, DateTimeOffset? before, CancellationToken cancellationToken)
    {
        int take = Math.Clamp(limit ?? 20, 1, 100);
        List<QuoteRequest> requests = await repository.ListAsync(take, before, cancellationToken);
        DateTimeOffset? nextBefore = requests.Count == take ? requests[^1].CreatedAt : (DateTimeOffset?)null;
        return TypedResults.Ok(new QuoteRequestPage(requests.Select(r => r.ToSummary()).ToList(), nextBefore));
    }
}

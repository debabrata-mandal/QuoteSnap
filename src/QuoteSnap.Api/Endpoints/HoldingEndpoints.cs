using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Events;
using QuoteSnap.Api.Live;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

/// <summary>Scraping the listed funds' portfolios from ScanX, and reading them back.</summary>
public static class HoldingEndpoints
{
    private const string RetryAfterSeconds = "1";

    public static IEndpointRouteBuilder MapHoldingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder fetches = app.MapGroup("/api/holdings/fetches").RequireApiKey().WithTags("Holdings");
        fetches.MapPost("/", StartAsync);
        fetches.MapGet("/latest", GetLatestAsync);
        fetches.MapGet("/{id:guid}", GetAsync);

        app.MapGet("/api/my-funds/{schemeCode:int}/holdings", GetHoldingsAsync).RequireApiKey().WithTags("Holdings");

        RouteGroupBuilder quotes = app.MapGroup("/api/holdings/quotes").RequireApiKey().WithTags("Holdings");
        quotes.MapPost("/", StartQuotesAsync);
        quotes.MapGet("/latest", GetLatestQuotesAsync);

        app.MapGet("/api/holdings/top", GetTopAsync).RequireApiKey().WithTags("Holdings");
        return app;
    }

    private const int DefaultTopLimit = 10;
    private const int MaxTopLimit = 100;

    /// <summary>The stocks the listed funds hold most, by weight averaged over the funds, with the last live prices.</summary>
    /// <param name="limit">At most 100; 10 by default.</param>
    private static async Task<Ok<TopHoldingList>> GetTopAsync(int? limit, HoldingRepository holdings, QuoteRequestRepository requests, LiveQuoteStore live, CancellationToken cancellationToken)
    {
        Dictionary<int, List<FundHoldingRecord>> portfolios = await holdings.GetLatestHoldingsAsync(cancellationToken);
        QuoteRequest? request = await requests.GetLatestFinishedForHoldingsAsync(cancellationToken);
        Dictionary<string, Price> prices = Prices.Merge(request?.Results, live.GetQuotes().Quotes);
        IReadOnlyList<TopHolding> top = TopHoldings.Rank(portfolios, prices, Math.Clamp(limit ?? DefaultTopLimit, 1, MaxTopLimit));
        return TypedResults.Ok(new TopHoldingList(portfolios.Count, request?.Id, Prices.LatestAt(request, prices), top));
    }

    /// <summary>
    /// Submits one quote request for the distinct stock symbols in the latest month of every listed fund. While such
    /// a request is queued or running, starting another returns that one instead.
    /// </summary>
    private static async Task<Results<Accepted<QuoteRequestAccepted>, Ok<QuoteRequestAccepted>, ProblemHttpResult>> StartQuotesAsync(HoldingRepository holdings, QuoteRequestRepository requests, QuoteRequestQueue queue, SessionService session, EventPublisher events, TimeProvider time, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (await requests.GetLatestForHoldingsAsync(includeResults: false, cancellationToken) is { } latest
            && !latest.Status.IsTerminal())
        {
            return TypedResults.Ok(new QuoteRequestAccepted(
                latest.Id, latest.Status, latest.TotalSymbols, [], $"/api/quote-requests/{latest.Id}", null));
        }

        List<string> symbols = await holdings.GetLatestSymbolsAsync(cancellationToken);
        if (symbols.Count == 0)
        {
            return TypedResults.Problem("No listed fund has stock holdings with a Fyers symbol. Fetch holdings with POST /api/holdings/fetches first.", statusCode: StatusCodes.Status400BadRequest, title: "No holdings symbols");
        }

        Results<Accepted<QuoteRequestAccepted>, ProblemHttpResult> submitted = await QuoteRequestEndpoints.SubmitAsync(symbols, forHoldings: true, requests, queue, session, events, time, loggers, cancellationToken);
        return submitted.Result switch
        {
            Accepted<QuoteRequestAccepted> accepted => accepted,
            ProblemHttpResult problem => problem,
            _ => throw new InvalidOperationException("Unexpected submit result."),
        };
    }

    /// <summary>The most recent quote request for the holdings, with its results, so a client can show the last LTPs.</summary>
    private static async Task<Results<Ok<QuoteRequestDetails>, ProblemHttpResult>> GetLatestQuotesAsync(QuoteRequestRepository requests, HttpContext http, CancellationToken cancellationToken)
    {
        if (await requests.GetLatestForHoldingsAsync(includeResults: true, cancellationToken) is not { } request)
            return TypedResults.Problem("No quotes have been requested for the holdings.", statusCode: StatusCodes.Status404NotFound);

        if (!request.Status.IsTerminal())
            http.Response.Headers.RetryAfter = RetryAfterSeconds;
        return TypedResults.Ok(request.ToDetails());
    }

    /// <summary>
    /// Starts fetching the holdings of every fund on the list. Poll the returned status URL for progress. While a
    /// fetch is queued or running, starting another returns that one instead.
    /// </summary>
    private static async Task<Results<Accepted<HoldingFetch>, Ok<HoldingFetch>, ProblemHttpResult>> StartAsync(HoldingRepository holdings, MyFundRepository funds, FyersSymbolRepository symbols, HoldingFetchQueue queue, EventPublisher events, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        List<Guid> unfinished = await holdings.GetUnfinishedFetchIdsAsync(cancellationToken);
        if (unfinished.Count > 0 && await holdings.GetFetchAsync(unfinished[^1], cancellationToken) is { } running)
            return TypedResults.Ok(ToDto(running));

        List<MyFundRecord> listed = await funds.GetAllAsync(cancellationToken);
        if (listed.Count == 0)
        {
            return TypedResults.Problem("There are no funds on the list. Add some with PUT /api/my-funds/{schemeCode} first.", statusCode: StatusCodes.Status400BadRequest, title: "No funds to fetch");
        }

        if ((await symbols.GetStatusAsync(cancellationToken)).SymbolCount == 0)
            return FyersSymbolEndpoints.NotLoaded();

        HoldingFetchRecord fetch = await holdings.CreateFetchAsync(listed, cancellationToken);
        queue.Enqueue(fetch.Id);
        loggers.CreateLogger(typeof(HoldingEndpoints)).LogInformation("Queued holdings fetch {FetchId} for {FundCount} funds", fetch.Id, listed.Count);
        await events.HoldingFetchUpdatedAsync(holdings, fetch.Id, cancellationToken);
        return TypedResults.Accepted($"/api/holdings/fetches/{fetch.Id}", ToDto(fetch));
    }

    private static async Task<Results<Ok<HoldingFetch>, ProblemHttpResult>> GetAsync(Guid id, HoldingRepository holdings, HttpContext http, CancellationToken cancellationToken)
    {
        if (await holdings.GetFetchAsync(id, cancellationToken) is not { } fetch)
            return TypedResults.Problem($"Holdings fetch {id} was not found.", statusCode: StatusCodes.Status404NotFound);
        return Respond(fetch, http);
    }

    /// <summary>The most recently started fetch, so a client can show the progress of one it didn't start.</summary>
    private static async Task<Results<Ok<HoldingFetch>, ProblemHttpResult>> GetLatestAsync(HoldingRepository holdings, HttpContext http, CancellationToken cancellationToken)
    {
        if (await holdings.GetLatestFetchAsync(cancellationToken) is not { } fetch)
            return TypedResults.Problem("Holdings have never been fetched.", statusCode: StatusCodes.Status404NotFound);
        return Respond(fetch, http);
    }

    /// <param name="month">The month to show, as 2026-09; the latest stored month by default.</param>
    private static async Task<Results<Ok<FundHoldings>, ProblemHttpResult>> GetHoldingsAsync(int schemeCode, string? month, MyFundRepository funds, HoldingRepository holdings, CancellationToken cancellationToken)
    {
        if (await funds.FindAsync(schemeCode, cancellationToken) is not { } fund)
            return TypedResults.Problem($"Scheme {schemeCode} isn't on the list.", statusCode: StatusCodes.Status404NotFound, title: "Fund not found");

        List<DateOnly> months = await holdings.GetMonthsAsync(schemeCode, cancellationToken);
        if (months.Count == 0 || fund.ScanxScheme is not { } scanxScheme || fund.HoldingsFetchedAt is not { } fetchedAt)
        {
            return TypedResults.Problem($"{fund.SchemeName} has no holdings yet. Fetch them with POST /api/holdings/fetches.", statusCode: StatusCodes.Status404NotFound, title: "No holdings");
        }

        DateOnly shown = months[0];
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateOnly.TryParseExact(month.Trim(), ["yyyy-MM", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
                return TypedResults.Problem($"'{month}' is not a month; use the form 2026-09.", statusCode: StatusCodes.Status400BadRequest);
            shown = new DateOnly(parsed.Year, parsed.Month, 1);
            if (!months.Contains(shown))
                return TypedResults.Problem($"No holdings are stored for {shown:yyyy-MM}.", statusCode: StatusCodes.Status404NotFound, title: "Month not stored");
        }

        List<FundHoldingRecord> current = await holdings.GetHoldingsAsync(schemeCode, shown, cancellationToken);
        DateOnly previousMonth = shown.AddMonths(-1);
        Dictionary<string, decimal> previous = months.Contains(previousMonth)
            ? (await holdings.GetHoldingsAsync(schemeCode, previousMonth, cancellationToken))
                .GroupBy(Identity)
                .ToDictionary(g => g.Key, g => g.Sum(h => h.Percent))
            : [];

        return TypedResults.Ok(new FundHoldings(
            schemeCode,
            fund.SchemeName,
            scanxScheme,
            shown,
            months,
            fetchedAt,
            [.. current.Select(h => new FundHolding(
                h.Name, h.Symbol, h.Isin, h.AssetType, h.Sector, h.Percent, h.Shares,
                previous.TryGetValue(Identity(h), out decimal before) ? before : null))]));
    }

    // Bonds and cash lines have no ISIN on ScanX, so they're told apart by name.
    private static string Identity(FundHoldingRecord h) => $"{h.AssetType}|{h.Isin ?? h.Name}";

    private static Ok<HoldingFetch> Respond(HoldingFetchRecord fetch, HttpContext http)
    {
        if (!fetch.Status.IsTerminal())
            http.Response.Headers.RetryAfter = RetryAfterSeconds;
        return TypedResults.Ok(ToDto(fetch));
    }

    public static HoldingFetch ToDto(this HoldingFetchRecord f) => new(
        f.Id,
        f.Status,
        f.CreatedAt,
        f.StartedAt,
        f.CompletedAt,
        f.Funds.Count,
        f.Funds.Count(x => x.Status.IsFinished()),
        f.Error,
        [.. f.Funds.OrderBy(x => x.Position).Select(x => new HoldingFetchFund(x.SchemeCode, x.SchemeName, x.Status, x.ScanxScheme, x.HoldingCount, x.Month, x.Error))]);
}

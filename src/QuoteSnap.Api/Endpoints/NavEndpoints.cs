using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Nav;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

public static class NavEndpoints
{
    public static IEndpointRouteBuilder MapNavEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/nav").RequireApiKey().WithTags("Mutual fund NAV");
        group.MapGet("/", GetAsync);
        group.MapGet("/search", SearchAsync);
        group.MapGet("/schemes", GetBySchemeCodeAsync);
        group.MapGet("/status", (NavRepository repository, CancellationToken cancellationToken) =>
            repository.GetStatusAsync(cancellationToken));
        group.MapPost("/refresh", RefreshAsync);
        return app;
    }

    private const int DefaultSearchLimit = 20;
    private const int MaxSearchLimit = 50;

    /// <param name="q">Part of a scheme or fund house name, in any order and spelling: "nippon power infra", "icici pru large mid".
    /// An ISIN or scheme code also works.</param>
    /// <param name="limit">At most 50; 20 by default.</param>
    private static async Task<Results<Ok<NavSearchResult>, ProblemHttpResult>> SearchAsync(string? q, int? limit, NavRepository repository, FundSearchCache search, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        string query = (q ?? "").Trim();
        if (query.Length < 2)
            return TypedResults.Problem("Provide a search of at least 2 characters in 'q'.", statusCode: StatusCodes.Status400BadRequest);

        if (await search.GetAsync(repository, cancellationToken) is not { } snapshot)
            return NotLoaded();

        FundSearchResult result = snapshot.Index.Search(query, Math.Clamp(limit ?? DefaultSearchLimit, 1, MaxSearchLimit));
        loggers.CreateLogger(typeof(NavEndpoints)).LogInformation("NAV search for {Query}: {FundCount} funds, partial matches only: {Partial}", query, result.Funds.Count, result.Partial);
        return TypedResults.Ok(new NavSearchResult(query, [.. result.Funds.Select(ToDto)], result.Partial, snapshot.LoadedAt));
    }

    /// <param name="code">One AMFI scheme code, or several separated by commas: ?code=120465 or ?code=120465,119018.</param>
    private static async Task<Results<Ok<NavLookup>, ProblemHttpResult>> GetBySchemeCodeAsync(string? code, NavRepository repository, CancellationToken cancellationToken)
    {
        string[] parts = (code ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<int> codes = [];
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                return TypedResults.Problem($"'{part}' is not a scheme code; codes are numbers such as 120465.", statusCode: StatusCodes.Status400BadRequest);
            if (!codes.Contains(value))
                codes.Add(value);
        }

        if (codes.Count == 0)
            return TypedResults.Problem("Provide at least one scheme 'code'.", statusCode: StatusCodes.Status400BadRequest);
        if (await repository.GetLoadedAtAsync(cancellationToken) is not { } loadedAt)
            return NotLoaded();

        Dictionary<int, MutualFundNav> found = (await repository.FindBySchemeCodesAsync(codes, cancellationToken)).ToDictionary(n => n.SchemeCode);
        return TypedResults.Ok(new NavLookup([.. codes.Where(found.ContainsKey).Select(c => ToDto(found[c]))], [.. codes.Where(c => !found.ContainsKey(c)).Select(c => c.ToString(CultureInfo.InvariantCulture))], loadedAt));
    }

    internal static ProblemHttpResult NotLoaded() => TypedResults.Problem("Mutual fund NAVs haven't been downloaded yet. Call POST /api/nav/refresh first.", statusCode: StatusCodes.Status503ServiceUnavailable, title: "NAVs not loaded");

    /// <param name="isin">One ISIN, or several separated by commas: ?isin=A or ?isin=A,B.</param>
    private static async Task<Results<Ok<NavLookup>, ProblemHttpResult>> GetAsync(string? isin, NavRepository repository, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ILogger logger = loggers.CreateLogger(typeof(NavEndpoints));
        List<string> requested = [.. (isin ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.ToUpperInvariant())
            .Distinct()];
        if (requested.Count == 0)
            return TypedResults.Problem("Provide at least one 'isin'.", statusCode: StatusCodes.Status400BadRequest);

        if (await repository.GetLoadedAtAsync(cancellationToken) is not { } loadedAt)
        {
            logger.LogWarning("NAV lookup for {IsinCount} ISINs refused: no NAVs have been downloaded yet", requested.Count);
            return NotLoaded();
        }

        List<MutualFundNav> matches = await repository.FindByIsinsAsync(requested, cancellationToken);
        List<FundNav> funds = [];
        HashSet<int> added = [];
        List<string> notFound = [];
        foreach (string code in requested)
        {
            List<MutualFundNav> forCode = [.. matches
                .Where(n => n.Isin == code || n.IsinReinvestment == code)
                .OrderBy(n => n.SchemeCode)];
            if (forCode.Count == 0)
                notFound.Add(code);
            funds.AddRange(forCode.Where(n => added.Add(n.SchemeCode)).Select(ToDto));
        }

        logger.LogInformation("NAV lookup for {IsinCount} ISINs: {FundCount} funds found, {NotFoundCount} ISINs not found (NAVs loaded {LoadedAt})", requested.Count, funds.Count, notFound.Count, loadedAt);
        return TypedResults.Ok(new NavLookup(funds, notFound, loadedAt));
    }

    private static async Task<Results<Ok<NavRefreshResult>, ProblemHttpResult>> RefreshAsync(NavImporter importer, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await importer.RefreshAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            loggers.CreateLogger(typeof(NavEndpoints)).LogWarning(
                "NAV refresh failed; the stored NAVs are unchanged. {ErrorType}: {Error}", ex.GetType().Name, ex.Message);
            return TypedResults.Problem($"Could not load the AMFI NAV file; the stored NAVs are unchanged. {ex.Message}", statusCode: StatusCodes.Status502BadGateway, title: "NAV download failed");
        }
    }

    internal static FundNav ToDto(MutualFundNav n) =>
        new(n.SchemeCode, n.Isin, n.IsinReinvestment, n.SchemeName, n.FundHouse, n.Category, n.Plan, n.Option, n.Nav, n.NavDate);
}

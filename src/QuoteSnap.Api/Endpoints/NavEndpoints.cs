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
        group.MapGet("/status", (NavRepository repository, CancellationToken cancellationToken) =>
            repository.GetStatusAsync(cancellationToken));
        group.MapPost("/refresh", RefreshAsync);
        return app;
    }

    /// <param name="isin">One ISIN, or several separated by commas: ?isin=A or ?isin=A,B.</param>
    private static async Task<Results<Ok<NavLookup>, ProblemHttpResult>> GetAsync(
        string? isin,
        NavRepository repository,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
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
            return TypedResults.Problem(
                "Mutual fund NAVs haven't been downloaded yet. Call POST /api/nav/refresh first.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "NAVs not loaded");
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

        logger.LogInformation(
            "NAV lookup for {IsinCount} ISINs: {FundCount} funds found, {NotFoundCount} ISINs not found (NAVs loaded {LoadedAt})",
            requested.Count, funds.Count, notFound.Count, loadedAt);
        return TypedResults.Ok(new NavLookup(funds, notFound, loadedAt));
    }

    private static async Task<Results<Ok<NavRefreshResult>, ProblemHttpResult>> RefreshAsync(
        NavImporter importer,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
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
            return TypedResults.Problem(
                $"Could not load the AMFI NAV file; the stored NAVs are unchanged. {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway,
                title: "NAV download failed");
        }
    }

    private static FundNav ToDto(MutualFundNav n) =>
        new(n.SchemeCode, n.Isin, n.IsinReinvestment, n.SchemeName, n.FundHouse, n.Category, n.Plan, n.Option, n.Nav, n.NavDate);
}

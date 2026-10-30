using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

/// <summary>The user's list of mutual funds, by AMFI scheme code, with their latest NAVs.</summary>
public static class MyFundEndpoints
{
    public static IEndpointRouteBuilder MapMyFundEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/my-funds").RequireApiKey().WithTags("My funds");
        group.MapGet("/", GetAllAsync);
        group.MapPut("/{schemeCode:int}", AddAsync);
        group.MapDelete("/{schemeCode:int}", RemoveAsync);
        return app;
    }

    private static async Task<Ok<MyFundList>> GetAllAsync(
        MyFundRepository funds,
        NavRepository navs,
        CancellationToken cancellationToken)
    {
        List<MyFundRecord> saved = await funds.GetAllAsync(cancellationToken);
        Dictionary<int, MutualFundNav> latest = saved.Count == 0
            ? []
            : (await navs.FindBySchemeCodesAsync([.. saved.Select(f => f.SchemeCode)], cancellationToken)).ToDictionary(n => n.SchemeCode);
        return TypedResults.Ok(new MyFundList(
            [.. saved.Select(f => ToDto(f, latest.GetValueOrDefault(f.SchemeCode)))],
            await navs.GetLoadedAtAsync(cancellationToken)));
    }

    /// <summary>Adds a Direct Plan scheme from AMFI's NAV file; adding a fund that's already on the list returns it unchanged.</summary>
    /// <param name="schemeCode">The AMFI scheme code, as returned by GET /api/nav/search.</param>
    private static async Task<Results<Created<MyFund>, Ok<MyFund>, ProblemHttpResult>> AddAsync(
        int schemeCode,
        MyFundRepository funds,
        NavRepository navs,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await navs.GetLoadedAtAsync(cancellationToken) is null)
            return NavEndpoints.NotLoaded();
        if ((await navs.FindBySchemeCodesAsync([schemeCode], cancellationToken)).SingleOrDefault() is not { } nav)
        {
            return TypedResults.Problem(
                $"Scheme {schemeCode} isn't among the Direct Plan schemes in AMFI's NAV file. Find funds with GET /api/nav/search.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Scheme not found");
        }

        DateTimeOffset now = time.GetUtcNow();
        (MyFundRecord fund, bool added) = await funds.AddAsync(new MyFundRecord
        {
            SchemeCode = nav.SchemeCode,
            SchemeName = nav.SchemeName,
            FundHouse = nav.FundHouse,
            Option = nav.Option,
            Category = nav.Category,
            // PostgreSQL keeps microseconds; truncating makes the returned value match what later reads return.
            AddedAt = now.AddTicks(-(now.Ticks % 10)),
        }, cancellationToken);

        MyFund dto = ToDto(fund, nav);
        if (!added)
            return TypedResults.Ok(dto);

        loggers.CreateLogger(typeof(MyFundEndpoints)).LogInformation(
            "Added {SchemeCode} {SchemeName} to my funds", fund.SchemeCode, fund.SchemeName);
        return TypedResults.Created($"/api/my-funds/{fund.SchemeCode}", dto);
    }

    /// <summary>Removes a fund from the list. Removing one that isn't on it also succeeds.</summary>
    private static async Task<NoContent> RemoveAsync(
        int schemeCode,
        MyFundRepository funds,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await funds.RemoveAsync(schemeCode, cancellationToken))
            loggers.CreateLogger(typeof(MyFundEndpoints)).LogInformation("Removed {SchemeCode} from my funds", schemeCode);
        return TypedResults.NoContent();
    }

    private static MyFund ToDto(MyFundRecord f, MutualFundNav? latest) =>
        new(f.SchemeCode, f.SchemeName, f.FundHouse, f.Option, f.Category, f.AddedAt, latest is null ? null : NavEndpoints.ToDto(latest));
}

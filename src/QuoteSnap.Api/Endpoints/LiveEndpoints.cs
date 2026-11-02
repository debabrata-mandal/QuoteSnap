using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Live;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

/// <summary>Prices of the top holdings streamed over the Fyers Data Socket.</summary>
public static class LiveEndpoints
{
    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/live").RequireApiKey().WithTags("Live");
        group.MapGet("/quotes", GetQuotes);
        group.MapGet("/status", GetStatus);
        return app;
    }

    /// <summary>The latest tick of each streamed holding, largest weight first. Kept after market hours until the next session re-ranks them.</summary>
    private static Ok<LiveQuoteList> GetQuotes(LiveQuoteStore store) => TypedResults.Ok(store.GetQuotes());

    private static Ok<LiveFeedStatus> GetStatus(LiveQuoteStore store) => TypedResults.Ok(store.GetStatus());
}

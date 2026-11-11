using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;
using Polly;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

public static class StatusEndpoints
{
    private static readonly string Version =
        typeof(StatusEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", async (SessionService session, TimeProvider time, CancellationToken cancellationToken) =>
                TypedResults.Ok(new ServiceStatus(Version, time.GetUtcNow(), await session.GetInfoAsync(cancellationToken))))
            .RequireApiKey()
            .WithTags("Status");

        app.MapGet("/api/market/status", GetMarketStatusAsync)
            .RequireApiKey()
            .WithTags("Status");

        app.MapHealthChecks("/health");
        return app;
    }

    /// <summary>
    /// Asks Fyers whether NSE's capital market is trading now, reusing an answer up to a minute old; a rejected access
    /// token is refreshed once.
    /// </summary>
    private static async Task<Results<Ok<MarketStatus>, ProblemHttpResult>> GetMarketStatusAsync(SessionService session, IQuoteProvider provider, MarketStatusCache cache, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await cache.GetAsync(ct => ReadMarketStatusAsync(session, provider, ct), cancellationToken));
        }
        catch (FyersReloginRequiredException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Fyers re-authentication required");
        }
        catch (FyersAuthException)
        {
            return TypedResults.Problem(FyersReloginRequiredException.DefaultMessage, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Fyers re-authentication required");
        }
        catch (Exception ex) when (ex is FyersApiException or HttpRequestException or ExecutionRejectedException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            loggers.CreateLogger(typeof(StatusEndpoints)).LogWarning("Couldn't read the market status from Fyers: {Error}", ex.Message);
            return TypedResults.Problem($"Fyers couldn't say whether the market is open: {ex.Message}", statusCode: StatusCodes.Status502BadGateway, title: "Market status unavailable");
        }
    }

    internal static async Task<string?> ReadMarketStatusAsync(SessionService session, IQuoteProvider provider, CancellationToken cancellationToken)
    {
        AccessTokenLease lease = await session.GetAccessTokenAsync(cancellationToken);
        try
        {
            return await provider.GetMarketStatusAsync(lease.AppId, lease.Token, cancellationToken);
        }
        catch (FyersAuthException)
        {
            lease = await session.RefreshAsync(lease.Version, cancellationToken);
            return await provider.GetMarketStatusAsync(lease.AppId, lease.Token, cancellationToken);
        }
    }
}

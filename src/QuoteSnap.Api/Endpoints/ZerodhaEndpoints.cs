using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Services;
using QuoteSnap.Api.Zerodha;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

/// <summary>Syncing the fund list with the Zerodha mutual fund holdings, from a fresh Kite login every time.</summary>
public static class ZerodhaEndpoints
{
    public static IEndpointRouteBuilder MapZerodhaEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/zerodha").RequireAdminKey().WithTags("Admin");
        group.MapGet("/login-url", GetLoginUrl);
        group.MapPost("/mf-sync", SyncAsync);
        return app;
    }

    // Parameters are nullable so a missing one gets the 400 below instead of a framework exception.
    private static Results<Ok<ZerodhaLoginUrl>, ProblemHttpResult> GetLoginUrl(string? apiKey, string? state, ZerodhaService zerodha)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return TypedResults.Problem("'apiKey' is required.", statusCode: StatusCodes.Status400BadRequest);
        if (state is not { Length: >= 16 and <= 128 })
            return TypedResults.Problem("'state' must be 16 to 128 characters.", statusCode: StatusCodes.Status400BadRequest);

        return TypedResults.Ok(new ZerodhaLoginUrl(zerodha.BuildLoginUrl(apiKey.Trim(), state)));
    }

    /// <summary>
    /// Logs in to Zerodha with the request token, reads the mutual fund holdings and logs out again, then makes the fund
    /// list match: adds held Direct Plan funds with their units, updates listed ones and removes funds no longer held.
    /// </summary>
    private static async Task<Results<Ok<ZerodhaMfSyncResult>, ProblemHttpResult>> SyncAsync(SyncZerodhaMutualFunds? body, ZerodhaService zerodha, NavRepository navs, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (body is null)
            return TypedResults.Problem("A JSON body with 'app' and 'requestToken' is required.", statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(body.RequestToken))
            return TypedResults.Problem("'requestToken' is required.", statusCode: StatusCodes.Status400BadRequest);
        if (body.App is not { } app || string.IsNullOrWhiteSpace(app.ApiKey) || string.IsNullOrWhiteSpace(app.ApiSecret))
            return TypedResults.Problem("'app' must include 'apiKey' and 'apiSecret'.", statusCode: StatusCodes.Status400BadRequest);
        // Checked before logging in: the request token works only once.
        if (await navs.GetLoadedAtAsync(cancellationToken) is null)
            return NavEndpoints.NotLoaded();

        ZerodhaAppCredentials credentials = new(app.ApiKey.Trim(), app.ApiSecret.Trim());
        try
        {
            return TypedResults.Ok(await zerodha.SyncMutualFundsAsync(credentials, body.RequestToken.Trim(), cancellationToken));
        }
        catch (ZerodhaAccountMismatchException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Wrong Zerodha account");
        }
        catch (KiteTokenException ex)
        {
            loggers.CreateLogger(typeof(ZerodhaEndpoints)).LogWarning("Zerodha login for API key {ApiKey} failed: {Error}", credentials.ApiKey, ex.Message);
            return TypedResults.Problem($"Zerodha rejected the login: {ex.Message} Log in again.", statusCode: StatusCodes.Status400BadRequest, title: "Zerodha login failed");
        }
        catch (Exception ex) when (ex is KiteApiException or HttpRequestException)
        {
            loggers.CreateLogger(typeof(ZerodhaEndpoints)).LogWarning("Zerodha sync failed: {Error}", ex.Message);
            return TypedResults.Problem($"Zerodha's mutual fund holdings could not be read: {ex.Message}", statusCode: StatusCodes.Status502BadGateway, title: "Zerodha request failed");
        }
    }
}

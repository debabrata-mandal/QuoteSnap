using Microsoft.AspNetCore.Http.HttpResults;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/admin/fyers").RequireAdminKey().WithTags("Admin");
        group.MapGet("/login-url", GetLoginUrl);
        group.MapPost("/session", SubmitLoginAsync);
        group.MapGet("/session", async (SessionService session, CancellationToken cancellationToken) =>
            TypedResults.Ok(await session.GetInfoAsync(cancellationToken)));
        group.MapDelete("/session", async (SessionService session, CancellationToken cancellationToken) =>
            TypedResults.Ok(await session.LogoutAsync(cancellationToken)));
        return app;
    }

    // Parameters are nullable so a missing one gets the 400 below instead of a framework exception.
    private static Results<Ok<FyersLoginUrl>, ProblemHttpResult> GetLoginUrl(string? appId, string? redirectUri, string? state, SessionService session)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return TypedResults.Problem("'appId' is required.", statusCode: StatusCodes.Status400BadRequest);
        if (redirectUri is null || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
            return TypedResults.Problem("'redirectUri' must be an absolute URL.", statusCode: StatusCodes.Status400BadRequest);
        if (state is not { Length: >= 16 and <= 128 })
            return TypedResults.Problem("'state' must be 16 to 128 characters.", statusCode: StatusCodes.Status400BadRequest);

        return TypedResults.Ok(new FyersLoginUrl(session.BuildLoginUrl(appId.Trim(), redirectUri, state)));
    }

    private static async Task<Results<Ok<FyersSessionInfo>, ProblemHttpResult>> SubmitLoginAsync(SubmitFyersLogin? body, SessionService session, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        ILogger logger = loggers.CreateLogger(typeof(AdminEndpoints));
        if (body is null)
            return TypedResults.Problem("A JSON body with 'app' and 'authCode' is required.", statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(body.AuthCode))
            return TypedResults.Problem("'authCode' is required.", statusCode: StatusCodes.Status400BadRequest);
        if (body.App is not { } app
            || string.IsNullOrWhiteSpace(app.AppId)
            || string.IsNullOrWhiteSpace(app.SecretId)
            || string.IsNullOrWhiteSpace(app.Pin))
        {
            return TypedResults.Problem("'app' must include 'appId', 'secretId' and 'pin'.", statusCode: StatusCodes.Status400BadRequest);
        }

        FyersAppCredentials credentials = new(app.AppId.Trim(), app.SecretId.Trim(), app.Pin.Trim());
        try
        {
            return TypedResults.Ok(await session.CompleteLoginAsync(credentials, body.AuthCode.Trim(), cancellationToken));
        }
        catch (FyersAccountMismatchException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Wrong Fyers account");
        }
        catch (FyersApiException ex)
        {
            logger.LogWarning("Fyers login for app {AppId} failed: {Error}", credentials.AppId, ex.Message);
            return TypedResults.Problem($"Fyers rejected the login: {ex.Message}", statusCode: StatusCodes.Status400BadRequest, title: "Fyers login failed");
        }
    }
}

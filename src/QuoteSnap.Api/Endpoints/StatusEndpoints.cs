using System.Reflection;
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

        app.MapHealthChecks("/health");
        return app;
    }
}

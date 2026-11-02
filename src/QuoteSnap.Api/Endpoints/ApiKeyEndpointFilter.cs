using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Options;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

public sealed class ApiKeyEndpointFilter(string headerName, Func<AuthOptions, string> selectExpectedKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        string expected = selectExpectedKey(http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value);
        string provided = http.Request.Headers[headerName].ToString();

        if (!KeysMatch(provided, expected))
            return TypedResults.Problem($"Missing or invalid {headerName} header.", statusCode: StatusCodes.Status401Unauthorized);

        return await next(context);
    }

    internal static bool KeysMatch(string provided, string expected) =>
        expected.Length > 0
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}

public static class ApiKeyEndpointFilterExtensions
{
    /// <summary>
    /// Endpoint filters don't run for SignalR hubs, so requests under <paramref name="path"/> are checked by middleware.
    /// Browsers can't set headers on WebSockets; the desktop client can, so only the header is accepted.
    /// </summary>
    public static IApplicationBuilder RequireApiKey(this IApplicationBuilder app, PathString path) =>
        app.UseWhen(c => c.Request.Path.StartsWithSegments(path), branch => branch.Use(async (http, next) =>
        {
            string expected = http.RequestServices.GetRequiredService<IOptions<AuthOptions>>().Value.ApiKey;
            if (ApiKeyEndpointFilter.KeysMatch(http.Request.Headers[QuoteSnapHeaders.ApiKey].ToString(), expected))
                await next(http);
            else
                await Results.Problem($"Missing or invalid {QuoteSnapHeaders.ApiKey} header.", statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(http);
        }));

    public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new ApiKeyEndpointFilter(QuoteSnapHeaders.ApiKey, o => o.ApiKey));

    public static TBuilder RequireAdminKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new ApiKeyEndpointFilter(QuoteSnapHeaders.AdminKey, o => o.AdminApiKey));
}

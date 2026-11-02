using System.Diagnostics;

namespace QuoteSnap.Api.Logging;

/// <summary>
/// Logs every outgoing call with its status and duration. Only the method and path are logged: query strings and
/// headers can carry tokens.
/// </summary>
public sealed class ExternalCallLoggingHandler(string service, ILogger<ExternalCallLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string method = request.Method.Method;
        string path = request.RequestUri?.AbsolutePath ?? "";
        long start = Stopwatch.GetTimestamp();
        try
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            logger.Log(
                response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning,
                "{Service} {Method} {Path} responded {StatusCode} in {ElapsedMs:0} ms",
                service, method, path, (int)response.StatusCode, elapsedMs);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("{Service} {Method} {Path} was cancelled", service, method, path);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "{Service} {Method} {Path} failed after {ElapsedMs:0} ms: {ErrorType}: {Error}",
                service, method, path, Stopwatch.GetElapsedTime(start).TotalMilliseconds, ex.GetType().Name, ex.Message);
            throw;
        }
    }
}

public static class ExternalCallLogging
{
    public static IHttpClientBuilder AddExternalCallLogging(this IHttpClientBuilder builder, string service) =>
        builder.AddHttpMessageHandler(services =>
            new ExternalCallLoggingHandler(service, services.GetRequiredService<ILogger<ExternalCallLoggingHandler>>()));
}

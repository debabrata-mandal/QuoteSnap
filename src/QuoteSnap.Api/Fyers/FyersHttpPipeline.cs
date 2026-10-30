using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Logging;

namespace QuoteSnap.Api.Fyers;

/// <summary>Shared by every Fyers HTTP client, because Fyers limits are per app and user, not per endpoint.</summary>
public sealed class FyersRateLimiter : IDisposable
{
    private static readonly TimeSpan NotableWait = TimeSpan.FromSeconds(1);

    private readonly RateLimiter _perSecond;
    private readonly RateLimiter _perMinute;
    private readonly FyersOptions _options;
    private readonly ILogger<FyersRateLimiter> _logger;

    public FyersRateLimiter(IOptions<FyersOptions> options, ILogger<FyersRateLimiter> logger)
    {
        FyersOptions o = options.Value;
        _options = o;
        _logger = logger;
        _perSecond = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = o.MaxRequestsPerSecond,
            TokensPerPeriod = o.MaxRequestsPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
        _perMinute = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = o.MaxRequestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        using RateLimitLease minute = await _perMinute.AcquireAsync(1, cancellationToken);
        using RateLimitLease second = await _perSecond.AcquireAsync(1, cancellationToken);
        if (!minute.IsAcquired || !second.IsAcquired)
            throw new InvalidOperationException("Could not acquire a Fyers rate limit permit.");

        TimeSpan waited = Stopwatch.GetElapsedTime(start);
        if (waited >= NotableWait)
        {
            _logger.LogInformation(
                "Held a Fyers call for {WaitedMs:0} ms to stay within {PerSecond} calls/s and {PerMinute} calls/min",
                waited.TotalMilliseconds, _options.MaxRequestsPerSecond, _options.MaxRequestsPerMinute);
        }
        else if (waited > TimeSpan.Zero)
        {
            _logger.LogDebug("Held a Fyers call for {WaitedMs:0} ms for the rate limit", waited.TotalMilliseconds);
        }
    }

    public void Dispose()
    {
        _perSecond.Dispose();
        _perMinute.Dispose();
    }
}

public sealed class FyersRateLimitHandler(FyersRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

public static class FyersHttpPipeline
{
    public static IServiceCollection AddFyersClients(this IServiceCollection services)
    {
        services.AddSingleton<FyersRateLimiter>();
        services.AddTransient<FyersRateLimitHandler>();
        services.AddHttpClient<FyersAuthClient>().AddFyersPipeline();
        services.AddHttpClient<IQuoteProvider, FyersQuoteProvider>().AddFyersPipeline();
        return services;
    }

    private static IHttpClientBuilder AddFyersPipeline(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            // 429s are not retried: repeated per-minute violations can get the Fyers account blocked for the day.
            options.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
            {
                { Exception: HttpRequestException or Polly.Timeout.TimeoutRejectedException } => true,
                { Result: { } response } => (int)response.StatusCode >= 500,
                _ => false,
            });
            options.Retry.DisableForUnsafeHttpMethods();
        });

        // Added after the resilience handler so every retry attempt also waits for a permit, and is logged
        // separately with only its own HTTP time.
        builder.AddHttpMessageHandler<FyersRateLimitHandler>();
        builder.AddExternalCallLogging("Fyers");
        return builder;
    }
}

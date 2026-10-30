using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Fyers;

/// <summary>Shared by every Fyers HTTP client, because Fyers limits are per app and user, not per endpoint.</summary>
public sealed class FyersRateLimiter : IDisposable
{
    private readonly RateLimiter _perSecond;
    private readonly RateLimiter _perMinute;

    public FyersRateLimiter(IOptions<FyersOptions> options)
    {
        FyersOptions o = options.Value;
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
        using RateLimitLease minute = await _perMinute.AcquireAsync(1, cancellationToken);
        using RateLimitLease second = await _perSecond.AcquireAsync(1, cancellationToken);
        if (!minute.IsAcquired || !second.IsAcquired)
            throw new InvalidOperationException("Could not acquire a Fyers rate limit permit.");
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

        // Added after the resilience handler so every retry attempt also waits for a permit.
        builder.AddHttpMessageHandler<FyersRateLimitHandler>();
        return builder;
    }
}

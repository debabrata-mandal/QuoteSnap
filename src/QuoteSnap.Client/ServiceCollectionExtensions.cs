using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace QuoteSnap.Client;

public static class ServiceCollectionExtensions
{
    public static IHttpClientBuilder AddQuoteSnapClient(this IServiceCollection services, Action<QuoteSnapClientOptions> configure)
    {
        services.Configure(configure);
        services.AddTransient<FyersLoginFlow>();
        services.AddTransient<ZerodhaMfSyncFlow>();
        return services.AddHttpClient<QuoteSnapClient>((provider, http) =>
            http.BaseAddress = provider.GetRequiredService<IOptions<QuoteSnapClientOptions>>().Value.BaseAddress);
    }
}

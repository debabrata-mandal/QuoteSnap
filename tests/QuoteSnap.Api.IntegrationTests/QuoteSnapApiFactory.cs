using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Api.Nav;
using QuoteSnap.Api.Services;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

/// <summary>Runs the real API against its own PostgreSQL database and a fake Fyers quote provider.</summary>
public sealed class QuoteSnapApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-0123456789";
    public const string AdminApiKey = "test-admin-key-0123456789";

    public static readonly FyersAppCredentials App = new("TEST-100", "test-secret", "0000");

    private readonly bool _ownsDataDirectory;

    /// <param name="dataDirectory">
    /// Pass the same directory to two factories to simulate a restart: they share the database and encryption keys.
    /// </param>
    /// <param name="delayBetweenBatchesMs">Zero by default so tests aren't slowed down by the production pause.</param>
    public QuoteSnapApiFactory(string? dataDirectory = null, bool startWorker = true, int delayBetweenBatchesMs = 0)
    {
        _ownsDataDirectory = dataDirectory is null;
        DataDirectory = dataDirectory ?? NewDataDirectory();
        StartWorker = startWorker;
        DelayBetweenBatchesMs = delayBetweenBatchesMs;
    }

    public string DataDirectory { get; }

    public bool StartWorker { get; }

    public int DelayBetweenBatchesMs { get; }

    public FakeQuoteProvider QuoteProvider => (FakeQuoteProvider)Services.GetRequiredService<IQuoteProvider>();

    /// <summary>What the fake AMFI download returns; null makes it fail with 503.</summary>
    public string? NavFile { get; set; }

    /// <summary>What the fake AMFI scheme master download returns; null makes it fail with 404.</summary>
    public string? SchemeMasterFile { get; set; }

    public static string NewDataDirectory() =>
        Path.Combine(Path.GetTempPath(), "quotesnap-tests", Guid.NewGuid().ToString("N"));

    public QuoteSnapClient CreateQuoteSnapClient() =>
        new(CreateClient(), Microsoft.Extensions.Options.Options.Create(new QuoteSnapClientOptions
        {
            ApiKey = ApiKey,
            AdminApiKey = AdminApiKey,
            MinPollInterval = TimeSpan.FromMilliseconds(20),
            MaxPollInterval = TimeSpan.FromMilliseconds(100),
        }));

    public async Task SeedSessionAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await scope.ServiceProvider.GetRequiredService<SessionStore>().SaveLoginAsync(
            new FyersSession("XT0001", App, "access-token", "refresh-token", now.AddHours(8), now.AddDays(15), now, 0),
            CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("QuoteSnap:DataDirectory", DataDirectory);
        builder.UseSetting(
            $"ConnectionStrings:{DatabaseSetup.ConnectionStringName}",
            PostgresServer.ConnectionStringFor("qs_" + Path.GetFileName(DataDirectory)));
        builder.UseSetting("Auth:ApiKey", ApiKey);
        builder.UseSetting("Auth:AdminApiKey", AdminApiKey);
        builder.UseSetting("Fyers:DelayBetweenBatchesMs", DelayBetweenBatchesMs.ToString(CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IQuoteProvider>();
            services.AddSingleton<IQuoteProvider, FakeQuoteProvider>();

            services.AddHttpClient<NavImporter>().ConfigurePrimaryHttpMessageHandler(() => new FakeAmfiHandler(this));

            if (!StartWorker)
                services.Remove(services.Single(d => d.ImplementationType == typeof(QuoteRequestWorker)));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_ownsDataDirectory)
            TryDeleteDirectory(DataDirectory);
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Succeeds for every symbol except those starting with NSE:FAIL.</summary>
public sealed class FakeQuoteProvider : IQuoteProvider
{
    public ConcurrentQueue<int> BatchSizes { get; } = new();

    /// <summary><see cref="Stopwatch.GetTimestamp"/> at each call.</summary>
    public ConcurrentQueue<long> CallTimestamps { get; } = new();

    public Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(
        string appId,
        string accessToken,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken)
    {
        if (appId != QuoteSnapApiFactory.App.AppId)
            throw new InvalidOperationException($"Expected the stored App ID, got '{appId}'.");

        CallTimestamps.Enqueue(Stopwatch.GetTimestamp());
        BatchSizes.Enqueue(symbols.Count);

        IReadOnlyList<ProviderQuote> quotes = symbols
            .Select(symbol => symbol.StartsWith("NSE:FAIL", StringComparison.Ordinal)
                ? ProviderQuote.Failure(symbol, "invalid symbol")
                : new ProviderQuote(symbol) { Ltp = 100.5m, Change = 1.25m, Volume = 1000, ExchangeTime = DateTimeOffset.UtcNow })
            .ToList();
        return Task.FromResult(quotes);
    }
}

/// <summary>
/// Serves the factory's current <see cref="QuoteSnapApiFactory.NavFile"/> and <see cref="QuoteSnapApiFactory.SchemeMasterFile"/>
/// as the AMFI downloads.
/// </summary>
internal sealed class FakeAmfiHandler(QuoteSnapApiFactory factory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool schemeMaster = request.RequestUri?.AbsolutePath.Contains("DownloadSchemeData", StringComparison.OrdinalIgnoreCase) == true;
        string? file = schemeMaster ? factory.SchemeMasterFile : factory.NavFile;
        return Task.FromResult(file is not null
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(file, Encoding.UTF8, "text/plain") }
            : new HttpResponseMessage(schemeMaster ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable));
    }
}

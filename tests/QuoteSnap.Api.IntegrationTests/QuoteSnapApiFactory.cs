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
using QuoteSnap.Api.Fyers;
using QuoteSnap.Api.Holdings;
using QuoteSnap.Api.Live;
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
    public QuoteSnapApiFactory(string? dataDirectory = null, bool startWorker = true, int delayBetweenBatchesMs = 0, bool liveFeed = false)
    {
        _ownsDataDirectory = dataDirectory is null;
        DataDirectory = dataDirectory ?? NewDataDirectory();
        StartWorker = startWorker;
        DelayBetweenBatchesMs = delayBetweenBatchesMs;
        LiveFeedEnabled = liveFeed;
    }

    public string DataDirectory { get; }

    private string ConnectionString => PostgresServer.ConnectionStringFor("qs_" + Path.GetFileName(DataDirectory));

    public bool StartWorker { get; }

    public int DelayBetweenBatchesMs { get; }

    /// <summary>Runs the live feed around the clock against <see cref="LiveFeed"/>; off by default.</summary>
    public bool LiveFeedEnabled { get; }

    public FakeLiveFeed LiveFeed => (FakeLiveFeed)Services.GetRequiredService<ILiveFeedConnectionFactory>();

    public FakeQuoteProvider QuoteProvider => (FakeQuoteProvider)Services.GetRequiredService<IQuoteProvider>();

    /// <summary>What the fake AMFI download returns; null makes it fail with 503.</summary>
    public string? NavFile { get; set; }

    /// <summary>What the fake AMFI scheme master download returns; null makes it fail with 404.</summary>
    public string? SchemeMasterFile { get; set; }

    /// <summary>What the fake ScanX AMC list returns; null makes it fail with 404.</summary>
    public string? ScanxSchemes { get; set; }

    /// <summary>The fake ScanX holdings pages by plan ID, from page 1. Unknown plans get ScanX's "no record" answer.</summary>
    public ConcurrentDictionary<int, string[]> ScanxHoldings { get; } = new();

    /// <summary>Plans whose holdings calls fail with 404.</summary>
    public ConcurrentDictionary<int, bool> FailingScanxPlans { get; } = new();

    /// <summary>The (plan ID, page) of each holdings call, in order.</summary>
    public ConcurrentQueue<(int PlanId, int Page)> ScanxHoldingsCalls { get; } = new();

    /// <summary>What the fake Fyers NSE_CM.csv returns; null makes it fail with 404.</summary>
    public string? NseSymbolMaster { get; set; }

    /// <summary>What the fake Fyers BSE_CM.csv returns; null makes it fail with 404.</summary>
    public string? BseSymbolMaster { get; set; }

    /// <summary>The (method, path, Authorization header) of each call to the Fyers auth API, in order.</summary>
    public ConcurrentQueue<(string Method, string Path, string? Authorization)> FyersAuthCalls { get; } = new();

    /// <summary>What the fake Fyers logout answers.</summary>
    public (HttpStatusCode Status, string Body) FyersLogoutResponse { get; set; } =
        (HttpStatusCode.OK, """{"s":"ok","code":200,"message":"Logged out successfully"}""");

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

    /// <summary>A hub connection over long polling, since the test server has no real sockets.</summary>
    public QuoteSnapHubClient CreateHubClient() =>
        new(new QuoteSnapClientOptions { BaseAddress = Server.BaseAddress, ApiKey = ApiKey, DefaultPollInterval = TimeSpan.FromMilliseconds(100) }, o =>
        {
            o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
        });

    public async Task SeedSessionAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await scope.ServiceProvider.GetRequiredService<SessionStore>().SaveLoginAsync(new FyersSession("XT0001", App, "access-token", "refresh-token", now.AddHours(8), now.AddDays(15), now, 0), CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("QuoteSnap:DataDirectory", DataDirectory);
        builder.UseSetting($"ConnectionStrings:{DatabaseSetup.ConnectionStringName}", ConnectionString);
        builder.UseSetting("Auth:ApiKey", ApiKey);
        builder.UseSetting("Auth:AdminApiKey", AdminApiKey);
        builder.UseSetting("Fyers:DelayBetweenBatchesMs", DelayBetweenBatchesMs.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Holdings:DelayBetweenRequestsMs", "0");
        builder.UseSetting("LiveQuotes:Enabled", LiveFeedEnabled.ToString());
        builder.UseSetting("LiveQuotes:CheckIntervalSeconds", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IQuoteProvider>();
            services.AddSingleton<IQuoteProvider, FakeQuoteProvider>();
            services.RemoveAll<ILiveFeedConnectionFactory>();
            services.AddSingleton<ILiveFeedConnectionFactory, FakeLiveFeed>();

            services.AddHttpClient<NavImporter>().ConfigurePrimaryHttpMessageHandler(() => new FakeAmfiHandler(this));
            services.AddHttpClient<ScanxClient>().ConfigurePrimaryHttpMessageHandler(() => new FakeScanxHandler(this));
            services.AddHttpClient<FyersSymbolImporter>().ConfigurePrimaryHttpMessageHandler(() => new FakeSymbolMasterHandler(this));
            services.AddHttpClient<FyersAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new FakeFyersAuthHandler(this));

            if (!StartWorker)
            {
                services.Remove(services.Single(d => d.ImplementationType == typeof(QuoteRequestWorker)));
                services.Remove(services.Single(d => d.ImplementationType == typeof(HoldingFetchWorker)));
            }
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        // Each test has its own database, and idle pooled connections otherwise stay open for minutes; together they
        // exceed the test server's 100-connection limit.
        using (Npgsql.NpgsqlConnection connection = new(ConnectionString))
            Npgsql.NpgsqlConnection.ClearPool(connection);
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

/// <summary>Opens fake sockets that authenticate at once and record what they're asked to stream.</summary>
public sealed class FakeLiveFeed : ILiveFeedConnectionFactory
{
    private ILiveFeedListener? _listener;

    /// <summary>"+symbols" per subscribe, "-symbols" per unsubscribe and "close", in order.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    public int Connections { get; private set; }

    /// <summary>Sends a tick on the latest socket.</summary>
    public void Tick(string symbol, decimal? ltp, decimal? change, decimal? changePercent) =>
        _listener!.OnTick(new LiveTick(symbol, ltp, change, changePercent));

    public ILiveFeedConnection Create(string appId, string accessToken, ILiveFeedListener listener)
    {
        if (appId != QuoteSnapApiFactory.App.AppId)
            throw new InvalidOperationException($"Expected the stored App ID, got '{appId}'.");
        _listener = listener;
        Connections++;
        return new Connection(this, listener);
    }

    private sealed class Connection(FakeLiveFeed feed, ILiveFeedListener listener) : ILiveFeedConnection
    {
        public void Connect() => listener.OnOpened();

        public void Subscribe(IReadOnlyList<string> symbols) => feed.Calls.Enqueue("+" + string.Join(",", symbols));

        public void Unsubscribe(IReadOnlyList<string> symbols) => feed.Calls.Enqueue("-" + string.Join(",", symbols));

        public void Dispose() => feed.Calls.Enqueue("close");
    }
}

/// <summary>Succeeds for every symbol except those starting with NSE:FAIL.</summary>
public sealed class FakeQuoteProvider : IQuoteProvider
{
    public ConcurrentQueue<int> BatchSizes { get; } = new();

    /// <summary><see cref="Stopwatch.GetTimestamp"/> at each call.</summary>
    public ConcurrentQueue<long> CallTimestamps { get; } = new();

    public Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(string appId, string accessToken, IReadOnlyList<string> symbols, CancellationToken cancellationToken)
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

/// <summary>
/// Serves <see cref="QuoteSnapApiFactory.ScanxSchemes"/> as amclist_v2 and <see cref="QuoteSnapApiFactory.ScanxHoldings"/> as
/// mfpastholdings_v2. Failures are 404s, which the resilience pipeline doesn't retry.
/// </summary>
internal sealed class FakeScanxHandler(QuoteSnapApiFactory factory) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? "";
        if (path.EndsWith("/amclist_v2", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            return factory.ScanxSchemes is { } schemes ? Json(schemes) : new HttpResponseMessage(HttpStatusCode.NotFound);

        if (!path.EndsWith("/mfpastholdings_v2", StringComparison.Ordinal) || request.Method != HttpMethod.Post || request.Content is null)
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        using System.Text.Json.JsonDocument body = System.Text.Json.JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        System.Text.Json.JsonElement query = body.RootElement.GetProperty("data");
        int planId = query.GetProperty("planid").GetInt32();
        int page = query.GetProperty("page").GetInt32();
        factory.ScanxHoldingsCalls.Enqueue((planId, page));

        if (factory.FailingScanxPlans.ContainsKey(planId))
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (!factory.ScanxHoldings.TryGetValue(planId, out string[]? pages))
            return Json("""{"code":-1,"data":"","remarks":"unable to fetch data, error :no record fetched"}""");
        return Json(page <= pages.Length
            ? pages[page - 1]
            : """{"code":0,"data":{},"headers":["sym","disp","assetPerc","date"],"meta":{"totalpages":1}}""");
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Serves <see cref="QuoteSnapApiFactory.NseSymbolMaster"/> and <see cref="QuoteSnapApiFactory.BseSymbolMaster"/>.</summary>
internal sealed class FakeSymbolMasterHandler(QuoteSnapApiFactory factory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? "";
        string? file = path.EndsWith("/NSE_CM.csv", StringComparison.Ordinal) ? factory.NseSymbolMaster
            : path.EndsWith("/BSE_CM.csv", StringComparison.Ordinal) ? factory.BseSymbolMaster
            : null;
        return Task.FromResult(file is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(file) });
    }
}

/// <summary>Answers the Fyers logout with <see cref="QuoteSnapApiFactory.FyersLogoutResponse"/>; other auth calls get 404.</summary>
internal sealed class FakeFyersAuthHandler(QuoteSnapApiFactory factory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? "";
        string? authorization = request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values) ? values.Single() : null;
        factory.FyersAuthCalls.Enqueue((request.Method.Method, path, authorization));

        if (path != "/api/v3/logout" || request.Method != HttpMethod.Post)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        (HttpStatusCode status, string body) = factory.FyersLogoutResponse;
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

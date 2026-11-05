using System.Security.Cryptography.X509Certificates;
using DotNetEnv;
using DotNetEnv.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Api.Events;
using QuoteSnap.Api.Fyers;
using QuoteSnap.Api.Holdings;
using QuoteSnap.Api.Live;
using QuoteSnap.Api.Logging;
using QuoteSnap.Api.Nav;
using QuoteSnap.Api.Options;
using QuoteSnap.Api.Services;
using QuoteSnap.Contracts;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Local secrets come from the repository's .env (gitignored), shared with docker compose.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddDotNetEnv(".env", LoadOptions.NoEnvVars().TraversePath());

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOptions<QuoteSnapOptions>()
    .BindConfiguration(QuoteSnapOptions.SectionName)
    .PostConfigure<IHostEnvironment>((options, env) =>
        options.DataDirectory = Path.GetFullPath(options.DataDirectory, env.ContentRootPath))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<FyersOptions>()
    .BindConfiguration(FyersOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AuthOptions>()
    .BindConfiguration(AuthOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<NavOptions>()
    .BindConfiguration(NavOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<HoldingsOptions>()
    .BindConfiguration(HoldingsOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<LiveQuotesOptions>()
    .BindConfiguration(LiveQuotesOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<QuoteSnapDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetQuoteSnapConnectionString()));
builder.Services.AddScoped<QuoteRequestRepository>();
builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<NavRepository>();
builder.Services.AddScoped<MyFundRepository>();
builder.Services.AddScoped<HoldingRepository>();
builder.Services.AddScoped<FyersSymbolRepository>();
builder.Services.AddScoped<SymbolExclusionRepository>();
builder.Services.AddSingleton<FundSearchCache>();

IDataProtectionBuilder dataProtection = builder.Services.AddDataProtection().SetApplicationName("QuoteSnap");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IOptions<QuoteSnapOptions>, ILoggerFactory>((keys, quoteSnap, loggerFactory) =>
        keys.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(quoteSnap.Value.KeysDirectory), loggerFactory));
if (builder.Configuration["QuoteSnap:DataProtectionCertificatePath"] is { Length: > 0 } certificatePath)
{
    dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(
        certificatePath, builder.Configuration["QuoteSnap:DataProtectionCertificatePassword"]));
}

builder.Services.AddFyersClients();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<QuoteRequestQueue>();
builder.Services.AddHostedService<QuoteRequestWorker>();
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddSingleton<HoldingFetchQueue>();
builder.Services.AddHostedService<HoldingFetchWorker>();
builder.Services.AddSingleton<LiveQuoteStore>();
builder.Services.AddSingleton<LiveFeedSignal>();
builder.Services.AddSingleton<ILiveFeedConnectionFactory, FyersLiveFeedConnectionFactory>();
builder.Services.AddHostedService<LiveFeedService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<HubPresence>();
builder.Services.AddSingleton<EventPublisher>();
builder.Services.AddHostedService<EventBroadcaster>();

// The NAV file is about 1.5 MB and the scheme master about 4 MB.
builder.Services.AddHttpClient<NavImporter>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuoteSnap");
    })
    .AddExternalCallLogging("AMFI");

IHttpClientBuilder scanx = builder.Services.AddHttpClient<ScanxClient>((services, client) =>
{
    client.BaseAddress = new Uri(services.GetRequiredService<IOptions<HoldingsOptions>>().Value.BaseUrl);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("QuoteSnap");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
scanx.AddStandardResilienceHandler();
scanx.AddExternalCallLogging("ScanX");

// The two symbol masters are about 2 MB each.
builder.Services.AddHttpClient<FyersSymbolImporter>((services, client) =>
    {
        client.BaseAddress = new Uri(services.GetRequiredService<IOptions<HoldingsOptions>>().Value.SymbolMasterBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(2);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuoteSnap");
    })
    .AddExternalCallLogging("Fyers symbol master");

builder.Services.AddProblemDetails();
// 3.0 rather than the 3.1 default: import tools such as Bruno and Postman read 3.0 reliably.
builder.Services.AddOpenApi(options => options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0);
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.RequireApiKey(QuoteSnapEvents.HubPath);

app.MapOpenApi();
app.MapQuoteRequestEndpoints();
app.MapStatusEndpoints();
app.MapAdminEndpoints();
app.MapNavEndpoints();
app.MapMyFundEndpoints();
app.MapHoldingEndpoints();
app.MapFyersSymbolEndpoints();
app.MapLiveEndpoints();
app.MapHub<EventHub>(QuoteSnapEvents.HubPath);

app.Run();

public partial class Program;

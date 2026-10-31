using DotNetEnv;
using DotNetEnv.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

string? command = args.FirstOrDefault()?.ToLowerInvariant();
bool manual = args.Contains("--manual", StringComparer.OrdinalIgnoreCase);
bool confirmed = args.Contains("--yes", StringComparer.OrdinalIgnoreCase) || args.Contains("-y", StringComparer.OrdinalIgnoreCase);
if (command is not ("login" or "status" or "logout"))
{
    PrintUsage();
    return 2;
}

IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddDotNetEnv(".env", LoadOptions.NoEnvVars().TraversePath())
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables("QUOTESNAP_")
    .Build();
ServiceSettings settings = configuration.GetSection("Service").Get<ServiceSettings>() ?? new ServiceSettings();
settings.AdminApiKey ??= configuration["Auth:AdminApiKey"];
if (string.IsNullOrWhiteSpace(settings.AdminApiKey))
{
    Console.Error.WriteLine("The admin API key is not configured. Set Auth__AdminApiKey in the repository's .env");
    Console.Error.WriteLine("(the same key the local API uses), or for another service the");
    Console.Error.WriteLine("QUOTESNAP_Service__AdminApiKey environment variable.");
    return 1;
}

using HttpClient http = new() { BaseAddress = settings.BaseUrl };
QuoteSnapClient client = new(http, Options.Create(new QuoteSnapClientOptions
{
    BaseAddress = settings.BaseUrl,
    AdminApiKey = settings.AdminApiKey,
}));

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    if (command == "status")
    {
        PrintSession(await client.GetFyersSessionAsync(cancellation.Token));
        return 0;
    }

    if (command == "logout")
        return await LogoutAsync(client, confirmed, cancellation.Token);

    FyersSettings fyers = configuration.GetSection("Fyers").Get<FyersSettings>() ?? new FyersSettings();
    FyersAppCredentials app = new(
        fyers.AppId is { Length: > 0 } appId ? appId : Prompt("Fyers App ID (e.g. XXXXXXXXXX-100): ", secret: false),
        fyers.SecretId is { Length: > 0 } secretId ? secretId : Prompt("Fyers Secret ID: ", secret: true),
        fyers.Pin is { Length: > 0 } pin ? pin : Prompt("Fyers PIN: ", secret: true));

    FyersLoginFlow flow = new(client);
    FyersSessionInfo session = manual
        ? await flow.LoginManualAsync(app, PromptForRedirectAsync, fyers.RedirectUri, cancellation.Token)
        : await flow.LoginWithBrowserAsync(
            app,
            new FyersLoginOptions
            {
                RedirectUri = fyers.RedirectUri,
                OpenBrowser = url =>
                {
                    Console.WriteLine("Opening the Fyers login page in your browser. If it doesn't open, visit:");
                    Console.WriteLine(url);
                    Console.WriteLine();
                    Console.WriteLine("Waiting for the login to complete (Ctrl+C to cancel)...");
                    FyersLoginOptions.DefaultOpenBrowser(url);
                },
            },
            cancellation.Token);

    Console.WriteLine("Login stored in QuoteSnap.");
    PrintSession(session);
    return 0;
}
catch (FyersLoginException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (QuoteSnapApiException ex)
{
    Console.Error.WriteLine($"QuoteSnap returned {(int)ex.StatusCode}: {ex.Message}");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach QuoteSnap at {settings.BaseUrl}: {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled. Nothing was changed.");
    return 1;
}

static async Task<int> LogoutAsync(QuoteSnapClient client, bool confirmed, CancellationToken cancellationToken)
{
    FyersSessionInfo session = await client.GetFyersSessionAsync(cancellationToken);
    if (session.FyersId is null)
    {
        Console.WriteLine("No Fyers login is stored in QuoteSnap; nothing to log out.");
        return 0;
    }

    if (!confirmed)
    {
        Console.Write($"Log {session.FyersId} out of QuoteSnap? Quote requests stop until you log in again. [y/N] ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes"))
        {
            Console.WriteLine("Nothing was changed.");
            return 1;
        }
    }

    FyersLogoutResult result = await client.LogoutFyersAsync(cancellationToken);
    Console.WriteLine($"Logged {result.FyersId ?? session.FyersId} out. QuoteSnap has forgotten the login, including the app details and PIN.");
    Console.WriteLine(result.RevokedAtFyers
        ? "  Fyers no longer accepts the access token."
        : $"  Fyers couldn't be asked to revoke the access token ({result.FyersError}). Nothing can use or renew it now, "
          + $"and it lapses by itself at {session.AccessTokenExpiresAt?.ToLocalTime():yyyy-MM-dd HH:mm}.");
    Console.WriteLine("  Run 'quotesnap-login login' to log in again.");
    return 0;
}

static Task<string> PromptForRedirectAsync(string loginUrl, CancellationToken cancellationToken)
{
    Console.WriteLine("Open this URL in a browser and log in to Fyers:");
    Console.WriteLine(loginUrl);
    Console.WriteLine();
    Console.Write("Then paste the full URL from the browser's address bar here: ");
    return Task.FromResult(Console.ReadLine() ?? "");
}

static void PrintSession(FyersSessionInfo session)
{
    Console.WriteLine($"  State:          {session.State}");
    if (session.FyersId is not null)
        Console.WriteLine($"  Fyers account:  {session.FyersId}");
    if (session.AccessTokenExpiresAt is { } access)
        Console.WriteLine($"  Access token:   valid until {access.ToLocalTime():yyyy-MM-dd HH:mm}");
    if (session.RefreshTokenExpiresAt is { } refresh)
    {
        int days = Math.Max(0, (int)(refresh - DateTimeOffset.UtcNow).TotalDays);
        Console.WriteLine($"  Refresh token:  valid until {refresh.ToLocalTime():yyyy-MM-dd HH:mm} ({days} days)");
    }

    if (session.State == FyersAuthState.ReloginRequired)
        Console.WriteLine("  Run 'quotesnap-login login' to log in again.");
}

static string Prompt(string label, bool secret)
{
    Console.Write(label);
    if (!secret)
        return (Console.ReadLine() ?? "").Trim();

    System.Text.StringBuilder value = new();
    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (value.Length > 0)
            {
                value.Length--;
                Console.Write("\b \b");
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            value.Append(key.KeyChar);
            Console.Write('*');
        }
    }

    Console.WriteLine();
    return value.ToString().Trim();
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  quotesnap-login login [--manual]   Log in to Fyers and store the session in QuoteSnap");
    Console.WriteLine("  quotesnap-login status             Show the stored Fyers session");
    Console.WriteLine("  quotesnap-login logout [--yes]     Revoke the Fyers access token and make QuoteSnap forget the login");
    Console.WriteLine();
    Console.WriteLine("--manual  Paste the redirect URL instead of using a local callback listener.");
    Console.WriteLine("--yes     Log out without asking first.");
}

internal sealed class ServiceSettings
{
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:8080/");

    /// <summary>Must match the service's Auth:AdminApiKey; falls back to Auth__AdminApiKey from .env.</summary>
    public string? AdminApiKey { get; set; }
}

/// <summary>The Fyers API app from myapi.fyers.in. Anything not configured is asked for at login.</summary>
internal sealed class FyersSettings
{
    public string? AppId { get; set; }

    public string? SecretId { get; set; }

    public string? Pin { get; set; }

    public string RedirectUri { get; set; } = FyersLoginOptions.DefaultRedirectUri;
}

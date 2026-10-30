using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Npgsql;
using QuoteSnap.Api.Options;

namespace QuoteSnap.Api.Data;

public static class DatabaseSetup
{
    public const string ConnectionStringName = "QuoteSnap";

    private const int MaxConnectAttempts = 30;
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(2);

    public static string GetQuoteSnapConnectionString(this IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not set. Set ConnectionStrings__{ConnectionStringName} "
                + "in the repository's .env locally (see .env.example), or as an environment variable in Docker.");

    /// <summary>Waits for PostgreSQL to accept connections (it may still be starting), then applies migrations.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        QuoteSnapOptions options = services.GetRequiredService<IOptions<QuoteSnapOptions>>().Value;
        Directory.CreateDirectory(options.DataDirectory);
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseSetup));

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        QuoteSnapDbContext db = scope.ServiceProvider.GetRequiredService<QuoteSnapDbContext>();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (NpgsqlException ex) when (ex.IsTransient && attempt < MaxConnectAttempts)
            {
                logger.LogWarning(
                    "PostgreSQL is not reachable yet ({Reason}); retrying in {Delay}s (attempt {Attempt} of {MaxAttempts})",
                    ex.Message, ConnectRetryDelay.TotalSeconds, attempt, MaxConnectAttempts);
                await Task.Delay(ConnectRetryDelay, cancellationToken);
            }
        }
    }
}

/// <summary>Used only by <c>dotnet ef</c> when generating migrations; it never connects.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<QuoteSnapDbContext>
{
    public QuoteSnapDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<QuoteSnapDbContext>().UseNpgsql("Host=localhost;Database=quotesnap").Options);
}

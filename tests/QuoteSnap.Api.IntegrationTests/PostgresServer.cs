using Npgsql;
using Testcontainers.PostgreSql;

namespace QuoteSnap.Api.IntegrationTests;

/// <summary>One PostgreSQL container for the whole test run (needs Docker); each factory gets its own database.</summary>
internal static class PostgresServer
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(() => Task.Run(StartAsync));

    public static string ConnectionStringFor(string database)
    {
        PostgreSqlContainer container = Container.Value.GetAwaiter().GetResult();
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database }.ConnectionString;
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync();
        return container;
    }
}

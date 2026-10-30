using System.Diagnostics;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Nav;

/// <summary>Downloads AMFI's NAV file and replaces the stored NAVs with its Direct Plan schemes.</summary>
public sealed class NavImporter(
    HttpClient http,
    NavRepository repository,
    IOptions<NavOptions> options,
    TimeProvider time,
    ILogger<NavImporter> logger)
{
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="FormatException">The file isn't in the expected format; the stored NAVs are kept.</exception>
    public async Task<NavRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        logger.LogInformation("Downloading mutual fund NAVs from {SourceUrl}", options.Value.SourceUrl);
        string text = await http.GetStringAsync(options.Value.SourceUrl, cancellationToken);

        AmfiNavFile file = AmfiNavParser.Parse(text);
        List<MutualFundNav> direct = [.. file.Navs.Where(IsDirectPlan)];
        logger.LogInformation(
            "Parsed the NAV file ({Characters:N0} characters): {SchemeCount} schemes, keeping {DirectCount} Direct Plan schemes; {Skipped} unreadable rows",
            text.Length, file.Navs.Count, direct.Count, file.SkippedRows);
        if (direct.Count == 0)
            throw new FormatException("The NAV file contained no Direct Plan schemes.");

        DateTimeOffset now = time.GetUtcNow();
        // PostgreSQL keeps microseconds; truncating makes the returned value match what lookups read back.
        DateTimeOffset loadedAt = now.AddTicks(-(now.Ticks % 10));
        foreach (MutualFundNav nav in direct)
            nav.LoadedAt = loadedAt;
        await repository.ReplaceAllAsync(direct, cancellationToken);

        DateOnly latest = direct.Max(n => n.NavDate);
        logger.LogInformation(
            "Stored {Count} Direct Plan NAVs (latest NAV date {LatestNavDate}) in {ElapsedMs:0} ms",
            direct.Count, latest, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return new NavRefreshResult(direct.Count, file.SkippedRows, latest, loadedAt);
    }

    // Older layouts of the file have no Plan column; their scheme names carry the plan instead.
    private static bool IsDirectPlan(MutualFundNav nav) => nav.Plan is { } plan
        ? plan.Contains("Direct", StringComparison.OrdinalIgnoreCase)
        : nav.SchemeName.Contains("Direct", StringComparison.OrdinalIgnoreCase);
}

using System.Diagnostics;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Nav;

/// <summary>
/// Downloads AMFI's NAV file and replaces the stored NAVs with its Direct Plan schemes, using the scheme master to place
/// schemes the NAV file lists without a plan.
/// </summary>
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
        Task<IReadOnlyDictionary<int, string>> navNamesDownload = DownloadNavNamesAsync(cancellationToken);
        string text = await http.GetStringAsync(options.Value.SourceUrl, cancellationToken);

        AmfiNavFile file = AmfiNavParser.Parse(text);
        LabelPlansFromNavNames(file.Navs, await navNamesDownload);
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

    /// <returns>Scheme master NAV names by scheme code, or none when the download fails; NAVs still refresh without them.</returns>
    private async Task<IReadOnlyDictionary<int, string>> DownloadNavNamesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.SchemeMasterUrl))
            return new Dictionary<int, string>();

        try
        {
            string text = await http.GetStringAsync(options.Value.SchemeMasterUrl, cancellationToken);
            return AmfiSchemeMaster.ParseNavNames(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex,
                "Couldn't read AMFI's scheme master from {SchemeMasterUrl}; schemes the NAV file lists without a plan are left out",
                options.Value.SchemeMasterUrl);
            return new Dictionary<int, string>();
        }
    }

    private void LabelPlansFromNavNames(IReadOnlyList<MutualFundNav> navs, IReadOnlyDictionary<int, string> navNames)
    {
        if (navNames.Count == 0)
            return;

        int labelled = 0;
        foreach (MutualFundNav nav in navs)
        {
            if (nav.Plan is null && navNames.TryGetValue(nav.SchemeCode, out string? navName) && AmfiSchemeMaster.IsDirect(navName))
            {
                nav.Plan = "Direct Plan";
                nav.Option ??= AmfiSchemeMaster.OptionFrom(navName, nav.SchemeName);
                labelled++;
            }
        }

        logger.LogInformation(
            "Read {NavNameCount} scheme names from the scheme master; {Labelled} schemes without a plan in the NAV file are Direct Plans",
            navNames.Count, labelled);
    }

    // Older layouts of the file have no Plan column; their scheme names carry the plan instead.
    private static bool IsDirectPlan(MutualFundNav nav) => nav.Plan is { } plan
        ? plan.Contains("Direct", StringComparison.OrdinalIgnoreCase)
        : nav.SchemeName.Contains("Direct", StringComparison.OrdinalIgnoreCase);
}

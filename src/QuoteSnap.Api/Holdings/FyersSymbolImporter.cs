using System.Diagnostics;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Holdings;

/// <summary>
/// Downloads Fyers' NSE and BSE cash-market symbol masters (no login is needed) and replaces the stored symbols, giving
/// each listed ISIN its NSE symbol, or its BSE one when it isn't listed on NSE.
/// </summary>
public sealed class FyersSymbolImporter(HttpClient http, FyersSymbolRepository repository, TimeProvider time, ILogger<FyersSymbolImporter> logger)
{
    /// <exception cref="HttpRequestException">A download failed.</exception>
    /// <exception cref="FormatException">A file isn't a symbol master; the stored symbols are kept.</exception>
    public async Task<FyersSymbolRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        logger.LogInformation("Downloading Fyers symbol masters from {BaseUrl}", http.BaseAddress);
        Task<string> nseDownload = http.GetStringAsync("NSE_CM.csv", cancellationToken);
        Task<string> bseDownload = http.GetStringAsync("BSE_CM.csv", cancellationToken);
        await Task.WhenAll(nseDownload, bseDownload);

        Dictionary<string, FyersSymbol> nse = FyersSymbolMaster.Parse(await nseDownload);
        Dictionary<string, FyersSymbol> bse = FyersSymbolMaster.Parse(await bseDownload);
        List<FyersSymbol> symbols = FyersSymbolMaster.Combine(nse, bse);

        DateTimeOffset now = time.GetUtcNow();
        // PostgreSQL keeps microseconds; truncating makes the returned value match what the status reads back.
        DateTimeOffset loadedAt = now.AddTicks(-(now.Ticks % 10));
        foreach (FyersSymbol symbol in symbols)
            symbol.LoadedAt = loadedAt;
        await repository.ReplaceAllAsync(symbols, cancellationToken);

        int bseOnly = symbols.Count - nse.Count;
        logger.LogInformation(
            "Stored {Count} Fyers symbols ({NseCount} NSE, {BseOnlyCount} BSE only) in {ElapsedMs:0} ms",
            symbols.Count, nse.Count, bseOnly, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return new FyersSymbolRefreshResult(symbols.Count, nse.Count, bseOnly, loadedAt);
    }
}

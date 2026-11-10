using System.Globalization;
using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Text;

/// <summary>Labels for fund holdings and the progress of fetching them.</summary>
public static class HoldingText
{
    /// <summary>"Sep 2026".</summary>
    public static string Month(DateOnly month) => month.ToString("MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>"19.97%"; small positions keep enough digits to be told apart from zero ("0.0020%").</summary>
    public static string Weight(decimal percent) =>
        (Math.Abs(percent) >= 0.01m || percent == 0 ? percent.ToString("N2", CultureInfo.CurrentCulture) : percent.ToString("0.0###", CultureInfo.CurrentCulture)) + "%";

    /// <summary>The change in weight from the month before, in percentage points: "+0.44", "−1.74", "New" or a dash.</summary>
    /// <param name="previousMonthStored">Whether the month before is stored at all; without it nothing is new.</param>
    public static string Change(decimal percent, decimal? previous, bool previousMonthStored)
    {
        if (previous is not { } before)
            return previousMonthStored ? "New" : Display.Missing;

        decimal change = percent - before;
        string magnitude = Math.Abs(change).ToString("N2", CultureInfo.CurrentCulture);
        return change > 0 ? $"+{magnitude}" : change < 0 ? $"−{magnitude}" : magnitude;
    }

    /// <summary>"24 holdings · Sep 2026", or what's missing.</summary>
    public static string Summary(HoldingsSummary? summary) => summary is { } s
        ? $"{Display.Count(s.Count, "holding")} · {Month(s.Month)}"
        : "Holdings not fetched yet";

    public static string FundStatus(HoldingFetchFundStatus status) => status switch
    {
        HoldingFetchFundStatus.Pending => "Waiting",
        HoldingFetchFundStatus.Running => "Fetching",
        HoldingFetchFundStatus.Ok => "Updated",
        HoldingFetchFundStatus.NotFound => "Not on ScanX",
        _ => "Failed",
    };

    /// <summary>"Fetching 2 of 5 funds", then "Fetched 5 funds".</summary>
    public static string FetchTitle(HoldingFetch fetch) => fetch.Status switch
    {
        HoldingFetchStatus.Queued => "Waiting to start",
        HoldingFetchStatus.Running => $"Fetching {Math.Min(fetch.FinishedFunds + 1, fetch.TotalFunds)} of {Display.Count(fetch.TotalFunds, "fund")}",
        _ => $"Fetched {Display.Count(fetch.TotalFunds, "fund")}",
    };

    /// <summary>The fund being fetched, or how the fetch ended.</summary>
    public static string FetchDetail(HoldingFetch fetch)
    {
        if (!fetch.Status.IsTerminal())
        {
            return fetch.Funds.FirstOrDefault(f => f.Status == HoldingFetchFundStatus.Running) is { } running
                ? running.ScanxScheme ?? running.SchemeName
                : "Reading ScanX's list of schemes…";
        }

        if (fetch.Status == HoldingFetchStatus.Failed && fetch.Error is { } error && fetch.Funds.All(f => f.Status == HoldingFetchFundStatus.Error))
            return error;

        int ok = fetch.Funds.Count(f => f.Status == HoldingFetchFundStatus.Ok);
        List<string> parts = [$"Updated {Display.Count(ok, "fund")}"];
        int notFound = fetch.Funds.Count(f => f.Status == HoldingFetchFundStatus.NotFound);
        if (notFound > 0)
            parts.Add($"{notFound} not on ScanX");
        int failed = fetch.Funds.Count(f => f.Status == HoldingFetchFundStatus.Error);
        if (failed > 0)
            parts.Add($"{failed} failed");
        return string.Join(" · ", parts);
    }

    /// <summary>From 0 to 100; the fund being fetched counts as half done.</summary>
    public static double FetchPercent(HoldingFetch fetch) => fetch.TotalFunds == 0 || fetch.Status.IsTerminal()
        ? 100
        : 100.0 * (fetch.FinishedFunds + (fetch.Funds.Any(f => f.Status == HoldingFetchFundStatus.Running) ? 0.5 : 0)) / fetch.TotalFunds;

    /// <summary>"Getting prices: 120 of 450 stocks", then "Live prices of 450 stocks".</summary>
    public static string QuotesTitle(QuoteRequestDetails request)
    {
        int done = request.Results.Count(r => r.Status != QuoteResultStatus.Pending);
        return request.Status switch
        {
            QuoteRequestStatus.Queued => "Live prices: waiting to start",
            QuoteRequestStatus.Running => $"Getting prices: {done.ToString("N0", CultureInfo.CurrentCulture)} of {Display.Count(request.TotalSymbols, "stock")}",
            QuoteRequestStatus.Failed => "Couldn't get live prices",
            _ => $"Live prices of {Display.Count(request.TotalSymbols, "stock")}",
        };
    }

    /// <summary>When the request was made and, once it's finished, how many prices came back.</summary>
    public static string QuotesDetail(QuoteRequestDetails request, DateTime? today = null)
    {
        if (!request.Status.IsTerminal())
            return $"Requested {Display.WhenInSentence(request.CreatedAt, today)}";

        if (request.Status == QuoteRequestStatus.Failed && request.Error is { } error)
            return error;

        List<string> parts = [$"{request.SucceededCount.ToString("N0", CultureInfo.CurrentCulture)} prices"];
        if (request.FailedCount > 0)
            parts.Add($"{request.FailedCount.ToString("N0", CultureInfo.CurrentCulture)} failed");
        parts.Add($"as of {Display.WhenInSentence(request.CompletedAt ?? request.CreatedAt, today)}");
        return string.Join(" · ", parts);
    }

    public static double QuotesPercent(QuoteRequestDetails request) => request.TotalSymbols == 0 || request.Status.IsTerminal()
        ? 100
        : 100.0 * request.Results.Count(r => r.Status != QuoteResultStatus.Pending) / request.TotalSymbols;

    /// <summary>The day's change as "+1.32%" or "−0.82%".</summary>
    public static string DayChange(decimal? percent)
    {
        if (percent is not { } raw)
            return Display.Missing;
        decimal change = Math.Round(raw, 2);
        string magnitude = Math.Abs(change).ToString("N2", CultureInfo.CurrentCulture);
        return change > 0 ? $"+{magnitude}%" : change < 0 ? $"−{magnitude}%" : $"{magnitude}%";
    }

    /// <summary>Weights by asset type, largest first; beyond <paramref name="max"/> types the rest are summed as "Other".</summary>
    public static IReadOnlyList<(string Label, decimal Percent)> AssetMix(IEnumerable<FundHolding> holdings, int max = 4)
    {
        List<(string Label, decimal Percent)> types = [.. holdings
            .GroupBy(h => h.AssetType)
            .Select(g => (Label: g.Key, Percent: g.Sum(h => h.Percent)))
            .OrderByDescending(t => t.Percent)];
        if (types.Count <= max)
            return types;
        return [.. types.Take(max - 1), ("Other", types.Skip(max - 1).Sum(t => t.Percent))];
    }
}

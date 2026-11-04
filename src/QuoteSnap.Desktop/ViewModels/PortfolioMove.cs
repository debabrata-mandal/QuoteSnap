using System.Globalization;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>
/// Today's estimated move of all listed funds together, as if the same amount were invested in each: the average of
/// the funds' estimated NAV changes. Funds without an estimate (no holdings) are left out.
/// </summary>
public sealed record PortfolioMove(decimal ChangePercent, int EstimatedFunds, int TotalFunds, DateTimeOffset? PricesAt, NavEstimate Best, NavEstimate Worst)
{
    /// <summary>Null when no fund has an estimate yet.</summary>
    public static PortfolioMove? From(NavEstimateList list)
    {
        List<NavEstimate> estimated = [.. list.Funds.Where(f => f.ChangePercent is not null)];
        if (estimated.Count == 0)
            return null;

        decimal average = Math.Round(estimated.Average(f => f.ChangePercent!.Value), 2);
        return new PortfolioMove(average, estimated.Count, list.Funds.Count, list.PricesAt, estimated.MaxBy(f => f.ChangePercent)!, estimated.MinBy(f => f.ChangePercent)!);
    }

    /// <summary>"▲ 0.42%", "▼ 1.10%" or "0.00%".</summary>
    public string ChangeText => Arrowed(ChangePercent);

    /// <summary>"Average of 10 of 11 funds · prices from today, 11:52".</summary>
    public string Detail => $"Average of {EstimatedFunds.ToString("N0", CultureInfo.CurrentCulture)} of {Display.Count(TotalFunds, "fund")}" + (PricesAt is { } at ? $" · prices from {Display.WhenInSentence(at)}" : "");

    public string BestText => Arrowed(Best.ChangePercent!.Value);

    public string WorstText => Arrowed(Worst.ChangePercent!.Value);

    public decimal BestChange => Best.ChangePercent!.Value;

    public decimal WorstChange => Worst.ChangePercent!.Value;

    /// <summary>"₹1,099.24" or "—".</summary>
    public string BestNavText => Rupees(Best.EstimatedNav);

    public string WorstNavText => Rupees(Worst.EstimatedNav);

    private static string Rupees(decimal? nav) => nav is { } value ? "₹" + FundText.EstimatedNav(value) : Display.Missing;

    private static string Arrowed(decimal percent) => percent switch
    {
        > 0 => $"▲ {percent.ToString("N2", CultureInfo.CurrentCulture)}%",
        < 0 => $"▼ {Math.Abs(percent).ToString("N2", CultureInfo.CurrentCulture)}%",
        _ => "0.00%",
    };
}

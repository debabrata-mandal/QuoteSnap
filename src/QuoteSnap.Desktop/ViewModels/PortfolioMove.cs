using System.Globalization;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>
/// Today's estimated move of all listed funds together. Once every fund has units it's weighted by each fund's value
/// (units × latest NAV); until then it's the plain average of the funds' estimated NAV changes, as if the same amount
/// were invested in each. Funds without an estimate (no holdings) are left out.
/// </summary>
/// <param name="Value">With units: the value of the estimated funds at their latest NAV, in rupees.</param>
/// <param name="Gain">With units: today's estimated gain or loss on <paramref name="Value"/>, in rupees.</param>
public sealed record PortfolioMove(decimal ChangePercent, int EstimatedFunds, int TotalFunds, DateTimeOffset? PricesAt, NavEstimate Best, NavEstimate Worst, decimal? Value = null, decimal? Gain = null)
{
    /// <summary>Null when no fund has an estimate yet.</summary>
    public static PortfolioMove? From(NavEstimateList list)
    {
        List<NavEstimate> estimated = [.. list.Funds.Where(f => f.ChangePercent is not null)];
        if (estimated.Count == 0)
            return null;

        NavEstimate best = estimated.MaxBy(f => f.ChangePercent)!;
        NavEstimate worst = estimated.MinBy(f => f.ChangePercent)!;
        decimal value = estimated.Sum(f => f.Units * f.Nav ?? 0);
        if (list.Funds.All(f => f.Units > 0) && estimated.All(f => f.Nav is not null) && value > 0)
        {
            decimal gain = estimated.Sum(f => f.Units!.Value * f.Nav!.Value * f.ChangePercent!.Value / 100);
            return new PortfolioMove(Math.Round(gain / value * 100, 2), estimated.Count, list.Funds.Count, list.PricesAt, best, worst, Math.Round(value, 2), Math.Round(gain, 2));
        }

        decimal average = Math.Round(estimated.Average(f => f.ChangePercent!.Value), 2);
        return new PortfolioMove(average, estimated.Count, list.Funds.Count, list.PricesAt, best, worst);
    }

    /// <summary>Whether the move is weighted by value, with <see cref="Gain"/> known.</summary>
    public bool IsWeighted => Gain is not null;

    /// <summary>"▲ 0.42%", "▼ 1.10%" or "0.00%".</summary>
    public string ChangeText => Arrowed(ChangePercent);

    /// <summary>"Weighted by value of 10 of 11 funds · prices from today, 11:52", or "Average of …" without units.</summary>
    public string Detail => $"{(IsWeighted ? "Weighted by value of" : "Average of")} {EstimatedFunds.ToString("N0", CultureInfo.CurrentCulture)} of {Display.Count(TotalFunds, "fund")}" + (PricesAt is { } at ? $" · prices from {Display.WhenInSentence(at)}" : "");

    public string DetailTip => IsWeighted
        ? "Each fund's estimated NAV change, weighted by the value of your units at the latest NAV"
        : "The average of each fund's estimated NAV change, as if the same amount were invested in each fund. Enter the units you hold for every fund on the Mutual funds page to weight it by value.";

    /// <summary>"▲ ₹1,240 on ₹3,10,000"; empty without units.</summary>
    public string AmountText => Gain is { } gain && Value is { } value ? $"{Arrow(gain)}₹{Math.Abs(gain).ToString("N0", CultureInfo.CurrentCulture)} on ₹{value.ToString("N0", CultureInfo.CurrentCulture)}" : "";

    public string BestText => Arrowed(Best.ChangePercent!.Value);

    public string WorstText => Arrowed(Worst.ChangePercent!.Value);

    public decimal BestChange => Best.ChangePercent!.Value;

    public decimal WorstChange => Worst.ChangePercent!.Value;

    /// <summary>"₹1,099.24" or "—".</summary>
    public string BestNavText => Rupees(Best.EstimatedNav);

    public string WorstNavText => Rupees(Worst.EstimatedNav);

    private static string Rupees(decimal? nav) => nav is { } value ? "₹" + FundText.EstimatedNav(value) : Display.Missing;

    private static string Arrow(decimal value) => value switch { > 0 => "▲ ", < 0 => "▼ ", _ => "" };

    private static string Arrowed(decimal percent) => percent switch
    {
        > 0 => $"▲ {percent.ToString("N2", CultureInfo.CurrentCulture)}%",
        < 0 => $"▼ {Math.Abs(percent).ToString("N2", CultureInfo.CurrentCulture)}%",
        _ => "0.00%",
    };
}

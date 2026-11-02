using System.Globalization;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>One stock tile in the dashboard's top holdings.</summary>
/// <param name="largest">The largest weight in the list, which gets the full bar.</param>
/// <param name="fundCount">How many funds the weights are averaged over.</param>
public sealed class TopHoldingRow(int rank, TopHolding holding, decimal largest, int fundCount)
{
    private readonly TopHolding _holding = holding;
    private readonly decimal _largest = largest;
    private readonly int _fundCount = fundCount;

    public int Rank { get; } = rank;

    public string Name { get; } = holding.Name;

    public string Symbol { get; } = holding.Symbol;

    /// <summary>"Hindustan Aeronautics · NSE:HAL-EQ · Aerospace &amp; Defence".</summary>
    public string Tip { get; } = string.Join(" · ", new[] { holding.Name, holding.Symbol, holding.Sector }.OfType<string>());

    public decimal Percent { get; } = holding.Percent;

    public string WeightText { get; } = HoldingText.Weight(holding.Percent);

    /// <summary>The weight as a share of the largest one, 0 to 1, for the bar.</summary>
    public double BarFraction { get; } = largest > 0 && holding.Percent > 0 ? (double)(holding.Percent / largest) : 0;

    public int FundCount { get; } = holding.FundCount;

    /// <summary>"8 of 11 funds".</summary>
    public string FundsText { get; } = $"{holding.FundCount.ToString("N0", CultureInfo.CurrentCulture)} of {Display.Count(fundCount, "fund")}";

    public string FundsTip { get; } = $"Held by {Display.Count(holding.FundCount, "fund")}; the weight is averaged over all {Display.Count(fundCount, "fund")} with holdings";

    public decimal? Ltp { get; } = holding.Ltp;

    /// <summary>"₹4,850.10", or a dash without a live price.</summary>
    public string PriceText { get; } = holding.Ltp is { } ltp ? "₹" + ltp.ToString("N2", CultureInfo.CurrentCulture) : Display.Missing;

    public decimal? DayChangePercent { get; } = holding.ChangePercent;

    /// <summary>"▲ 1.76%", "▼ 0.41%", or a dash without a live price.</summary>
    public string DayChangeText { get; } = holding.ChangePercent switch
    {
        null => Display.Missing,
        > 0 and { } up => $"▲ {up.ToString("N2", CultureInfo.CurrentCulture)}%",
        < 0 and { } down => $"▼ {Math.Abs(down).ToString("N2", CultureInfo.CurrentCulture)}%",
        _ => "0.00%",
    };

    /// <summary>The same tile with a streamed price.</summary>
    public TopHoldingRow WithPrice(LiveQuote quote) => new(Rank, _holding with { Ltp = quote.Ltp, ChangePercent = quote.ChangePercent }, _largest, _fundCount);
}

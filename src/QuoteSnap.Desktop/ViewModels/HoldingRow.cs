using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>One line of a fund's portfolio in the holdings table.</summary>
public sealed class HoldingRow
{
    /// <summary>The widest weight bar, for the largest holding.</summary>
    public const double MaxBarWidth = 72;

    /// <param name="largest">The largest weight in the table, which gets the full bar.</param>
    /// <param name="previousMonthStored">Whether the month before is stored, so a holding without a previous weight is new.</param>
    public HoldingRow(FundHolding holding, decimal largest, bool previousMonthStored)
    {
        Name = holding.Name;
        Symbol = holding.Symbol;
        Isin = holding.Isin;
        AssetType = holding.AssetType;
        Sector = holding.Sector ?? Display.Missing;
        Percent = holding.Percent;
        Shares = holding.Shares;
        Change = holding.PreviousPercent is { } previous ? holding.Percent - previous : null;
        IsNew = holding.PreviousPercent is null && previousMonthStored;
        WeightText = HoldingText.Weight(holding.Percent);
        ChangeText = HoldingText.Change(holding.Percent, holding.PreviousPercent, previousMonthStored);
        BarWidth = largest > 0 && holding.Percent > 0 ? Math.Max(2, (double)(holding.Percent / largest) * MaxBarWidth) : 0;
    }

    public string Name { get; }

    public string? Symbol { get; }

    public string? Isin { get; }

    /// <summary>"HDFCBANK · INE040A01034", or the asset type for unlisted lines such as TREPS.</summary>
    public string Details => string.Join(" · ", new[] { Symbol, Isin }.OfType<string>()) is { Length: > 0 } details ? details : AssetType;

    public string AssetType { get; }

    public string Sector { get; }

    public decimal Percent { get; }

    public decimal? Shares { get; }

    /// <summary>Percentage points since the month before; null when there's nothing to compare with.</summary>
    public decimal? Change { get; }

    public bool IsNew { get; }

    public string WeightText { get; }

    public string ChangeText { get; }

    public double BarWidth { get; }

    public bool Matches(string filter) =>
        Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Symbol?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true
        || Isin?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
        || Sector.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || AssetType.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

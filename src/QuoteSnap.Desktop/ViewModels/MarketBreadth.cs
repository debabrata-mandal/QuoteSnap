using System.Globalization;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>How many of the stocks the funds hold are up, down or flat today. Stocks without a day change yet are left out.</summary>
public sealed record MarketBreadth(int Advances, int Declines, int Unchanged)
{
    /// <summary>Null when no stock has a day change yet.</summary>
    public static MarketBreadth? From(IEnumerable<decimal?> dayChanges)
    {
        List<decimal> known = [.. dayChanges.OfType<decimal>()];
        return known.Count == 0 ? null : new MarketBreadth(known.Count(c => c > 0), known.Count(c => c < 0), known.Count(c => c == 0));
    }

    public int Total => Advances + Declines + Unchanged;

    public string AdvancesText => $"▲ {Advances.ToString("N0", CultureInfo.CurrentCulture)}";

    public string DeclinesText => $"▼ {Declines.ToString("N0", CultureInfo.CurrentCulture)}";

    /// <summary>The advancing part of the up-and-down bar, 0 to 1; half when nothing moved.</summary>
    public double AdvanceShare => Advances + Declines == 0 ? 0.5 : (double)Advances / (Advances + Declines);

    /// <summary>"6 unchanged" or "of 560 stocks".</summary>
    public string Detail => Unchanged > 0 ? $"{Unchanged.ToString("N0", CultureInfo.CurrentCulture)} unchanged" : $"of {Total.ToString("N0", CultureInfo.CurrentCulture)} stocks";

    public string Tooltip => $"Of the {Total.ToString("N0", CultureInfo.CurrentCulture)} stocks your funds hold with a price today: {Advances.ToString("N0", CultureInfo.CurrentCulture)} up, {Declines.ToString("N0", CultureInfo.CurrentCulture)} down, {Unchanged.ToString("N0", CultureInfo.CurrentCulture)} unchanged";
}
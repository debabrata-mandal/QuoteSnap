using System.Globalization;
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
    /// <param name="quote">The holding's quote from the last live-prices request, if any.</param>
    public HoldingRow(FundHolding holding, decimal largest, bool previousMonthStored, QuoteResultDto? quote = null)
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
        if (quote is { Status: QuoteResultStatus.Ok })
        {
            Ltp = quote.Ltp;
            DayChangePercent = quote.ChangePercent;
        }

        LtpText = Symbol is null ? "" : Ltp?.ToString("N2", CultureInfo.CurrentCulture) ?? Display.Missing;
        DayChangeText = Symbol is null ? "" : HoldingText.DayChange(DayChangePercent);
        QuoteTip = quote switch
        {
            null => null,
            { Status: QuoteResultStatus.Ok } => $"{quote.Symbol} · fetched {Display.WhenInSentence(quote.FetchedAt)}",
            { Status: QuoteResultStatus.Pending } => $"{quote.Symbol} · waiting for its price",
            _ => $"{quote.Symbol} · {quote.Error ?? "no price"}",
        };
    }

    public string Name { get; }

    public string? Symbol { get; }

    public string? Isin { get; }

    /// <summary>"NSE:HDFCBANK-EQ · INE040A01034", or the asset type for unlisted lines such as TREPS.</summary>
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

    /// <summary>Last traded price from the last live-prices request; null without one.</summary>
    public decimal? Ltp { get; }

    public decimal? DayChangePercent { get; }

    /// <summary>The price, a dash when a listed stock has none, or empty for unlisted lines.</summary>
    public string LtpText { get; }

    public string DayChangeText { get; }

    /// <summary>The quote's symbol and when it was fetched, or why it has no price.</summary>
    public string? QuoteTip { get; }

    public bool Matches(string filter) =>
        Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Symbol?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true
        || Isin?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
        || Sector.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || AssetType.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

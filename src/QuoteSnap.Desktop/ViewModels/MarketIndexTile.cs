using System.Globalization;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>A market index on the dashboard's market strip; dashes until its first tick.</summary>
public sealed record MarketIndexTile(MarketIndex Index, LiveQuote? Quote)
{
    public string Name => Index.Name;

    /// <summary>"25,600.35", or a dash before the first tick.</summary>
    public string ValueText => Quote is { } quote ? quote.Ltp.ToString("N2", CultureInfo.CurrentCulture) : Display.Missing;

    public decimal? ChangePercent => Quote?.ChangePercent;

    /// <summary>The change as good (positive) or bad (negative) news, for its colour: a rising India VIX is bad news.</summary>
    public decimal? Tone => Index.FallIsGood ? -ChangePercent : ChangePercent;

    /// <summary>"▲ 0.40%", "▼ 0.11%", "0.00%", or a dash before the first tick.</summary>
    public string PercentText
    {
        get
        {
            if (Quote?.ChangePercent is not { } percent)
                return Display.Missing;
            string magnitude = Math.Abs(percent).ToString("N2", CultureInfo.CurrentCulture) + "%";
            return percent > 0 ? "▲ " + magnitude : percent < 0 ? "▼ " + magnitude : magnitude;
        }
    }

    /// <summary>"+102.50", "-61.20" or "0.00"; empty when there is no change in points.</summary>
    public string PointsText => Quote?.Change is { } change ? change.ToString("+#,##0.00;-#,##0.00;0.00", CultureInfo.CurrentCulture) : "";

    public string Tip => Quote is { } quote ? $"{Index.Symbol} · last tick {Display.WhenInSentence(quote.UpdatedAt)}" : $"{Index.Symbol} · waiting for the first tick";

    /// <summary>A tile for each of <see cref="MarketIndex.All"/>, with its quote if there is one.</summary>
    public static IReadOnlyList<MarketIndexTile> From(IReadOnlyDictionary<string, LiveQuote> quotes) => [.. MarketIndex.All.Select(i => new MarketIndexTile(i, quotes.GetValueOrDefault(i.Symbol)))];
}
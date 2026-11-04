using System.Globalization;
using System.Text.RegularExpressions;
using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Text;

/// <summary>Turns AMFI's inconsistent option and category text into short labels.</summary>
public static partial class FundText
{
    private static readonly (string Match, string Label)[] Frequencies =
    [
        ("daily", "Daily"),
        ("weekly", "Weekly"),
        ("fortnightly", "Fortnightly"),
        ("monthly", "Monthly"),
        ("quarterly", "Quarterly"),
        ("quaterly", "Quarterly"),
        ("half yearly", "Half-yearly"),
        ("half-yearly", "Half-yearly"),
        ("annual", "Annual"),
        ("yearly", "Annual"),
    ];

    private static readonly string[] AvatarColors =
        ["#6C8CFF", "#9B7BFF", "#22C55E", "#F59E0B", "#EF4444", "#38BDF8", "#EC4899", "#14B8A6", "#F97316", "#84CC16"];

    /// <summary>"GROWTH", "Growth Option" and "Direct Growth" become "Growth"; the many IDCW spellings become "IDCW" or "Monthly IDCW".</summary>
    public static string? Option(string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
            return null;

        string lower = option.ToLowerInvariant();
        bool idcw = lower.Contains("idcw") || lower.Contains("income distribution") || lower.Contains("dividend")
            || lower.Contains("payout") || lower.Contains("reinvest");
        if (idcw)
        {
            string? frequency = Frequencies.FirstOrDefault(f => lower.Contains(f.Match)).Label;
            return frequency is null ? "IDCW" : $"{frequency} IDCW";
        }

        return lower.Contains("growth") ? "Growth"
            : lower.Contains("bonus") ? "Bonus"
            : Display.ShortLabel(option.Replace("Direct", "", StringComparison.OrdinalIgnoreCase).Trim());
    }

    /// <summary>"Open Ended Schemes(Equity Scheme - Large &amp; Mid Cap Fund)" becomes ["Equity", "Large &amp; Mid Cap"].</summary>
    public static IReadOnlyList<string> Category(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return [];

        int open = category.IndexOf('(');
        int close = category.LastIndexOf(')');
        string inner = open >= 0 && close > open ? category[(open + 1)..close] : category;

        List<string> parts = [];
        foreach (string part in inner.Split(" - ", 2, StringSplitOptions.TrimEntries))
        {
            string cleaned = CategoryNoise().Replace(part, "").Replace("/ ", "/", StringComparison.Ordinal);
            cleaned = Spaces().Replace(cleaned, " ").Trim();
            if (cleaned.EndsWith(" Fund", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^" Fund".Length];
            if (cleaned.Length > 0 && !parts.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                parts.Add(cleaned);
        }

        return parts;
    }

    /// <summary>"Growth · Equity · Large &amp; Mid Cap".</summary>
    public static string Subtitle(string? option, string? category) =>
        string.Join(" · ", new[] { Option(option) }.Concat(Category(category)).Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>"Nippon India Mutual Fund" becomes "NI"; short all-caps houses keep their name ("DSP", "HDFC").</summary>
    public static string Initials(string? fundHouse, string schemeName)
    {
        string[] words = [.. (fundHouse ?? schemeName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Equals("Mutual", StringComparison.OrdinalIgnoreCase) && !w.Equals("Fund", StringComparison.OrdinalIgnoreCase))
            .Where(w => char.IsLetterOrDigit(w[0]))];
        return words switch
        {
            [] => "?",
            [string only] when only.All(char.IsUpper) => only.Length <= 4 ? only : only[..2],
            [string only] => only[..1].ToUpper(CultureInfo.InvariantCulture),
            [string first, string second, ..] => $"{char.ToUpperInvariant(first[0])}{char.ToUpperInvariant(second[0])}",
        };
    }

    /// <summary>A colour that stays the same for a fund house across runs.</summary>
    public static string AvatarColor(string? key)
    {
        uint hash = 2166136261;
        foreach (char c in (key ?? "").ToLowerInvariant())
            hash = (hash ^ c) * 16777619;
        return AvatarColors[hash % (uint)AvatarColors.Length];
    }

    /// <summary>"1,161.81" or "402.6856": at least 2 decimals, up to the 4 AMFI publishes.</summary>
    public static string Nav(decimal nav) => nav.ToString("#,##0.00##", CultureInfo.CurrentCulture);

    /// <summary>"1,102.5250": an estimate always shows 4 decimals, so a live tick changes digits in place rather than the text's length.</summary>
    public static string EstimatedNav(decimal nav) => nav.ToString("#,##0.0000", CultureInfo.CurrentCulture);

    /// <summary>
    /// How an estimate was made: "From 22 of 24 holdings (94.10% of assets) in the Sep 2026 portfolio · on the NAV of
    /// 31 Oct 2026 · prices today, 11:20:05", or why there isn't one.
    /// </summary>
    public static string EstimateTip(NavEstimate estimate, DateTimeOffset? pricesAt, DateTime? today = null)
    {
        if (estimate.HoldingsMonth is not { } month)
            return "No holdings yet. Fetch them on the Holdings page to estimate this fund.";
        if (pricesAt is null)
            return "No live prices yet. Press Get live prices to estimate today's NAV.";
        if (estimate.ChangePercent is null)
            return $"None of its {Display.Count(estimate.TotalHoldings, "holding")} has a live price, so it can't be estimated.";

        List<string> parts =
        [
            $"From {estimate.PricedHoldings.ToString("N0", CultureInfo.CurrentCulture)} of {Display.Count(estimate.TotalHoldings, "holding")} "
                + $"({estimate.PricedPercent.ToString("N2", CultureInfo.CurrentCulture)}% of assets) in the {HoldingText.Month(month)} portfolio; the rest count as unchanged",
        ];
        if (estimate.NavDate is { } navDate)
            parts.Add($"on the NAV of {Display.Date(navDate)}");
        parts.Add($"prices {Display.WhenInSentence(pricesAt, today)}");
        return string.Join(" · ", parts);
    }

    [GeneratedRegex(@"\b(Schemes?|Oriented)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryNoise();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}

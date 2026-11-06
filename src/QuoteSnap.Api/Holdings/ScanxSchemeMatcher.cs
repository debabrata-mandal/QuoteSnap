using System.Text;
using System.Text.RegularExpressions;

namespace QuoteSnap.Api.Holdings;

/// <summary>
/// Finds the ScanX scheme for an AMFI scheme. ScanX names only Direct-Growth plans ("DSP Midcap Direct Plan-Growth"),
/// while AMFI names the scheme alone ("DSP Midcap Fund"), so names are compared without plan, option and filler words,
/// and with spacing ignored ("Midcap 150" and "Midcap150").
/// </summary>
public static partial class ScanxSchemeMatcher
{
    /// <summary>Close enough to be a spelling variant; "Large Cap" and "Large &amp; Mid Cap" score well below this.</summary>
    private const double MinSimilarity = 0.9;

    private static readonly HashSet<string> Filler =
    [
        "direct", "plan", "growth", "directgrowth", "option", "regular", "idcw", "dividend", "payout", "reinvestment", "fund", "scheme",
        "the", "and",
    ];

    /// <param name="fundHouse">AMFI's fund house, e.g. "PPFAS Mutual Fund"; limits the search to that AMC when ScanX has it.</param>
    /// <returns>Null when no ScanX scheme is close enough.</returns>
    public static ScanxScheme? Match(string schemeName, string? fundHouse, IReadOnlyList<ScanxScheme> schemes)
    {
        List<string> words = Words(schemeName);
        if (words.Count == 0)
            return null;

        List<ScanxScheme> sameAmc = [];
        if (fundHouse is not null)
        {
            List<string> amc = AmcWords(fundHouse);
            sameAmc = [.. schemes.Where(s => string.Concat(AmcWords(s.Amc)) == string.Concat(amc))];
            // AMFI may rename a fund house by adding to its name ("Zerodha CASE Mutual Fund", still "Zerodha Mutual Fund"
            // on ScanX), and its funds' names carry the addition too: "Zerodha CASE Nifty 50 Index Fund".
            if (sameAmc.Count == 0 && ScanxAmc(amc, schemes) is { } scanxAmc)
            {
                sameAmc = [.. schemes.Where(s => AmcWords(s.Amc).SequenceEqual(scanxAmc))];
                if (words.Take(amc.Count).SequenceEqual(amc))
                    words = [.. scanxAmc, .. words.Skip(amc.Count)];
            }
        }

        string key = string.Concat(words);
        IReadOnlyList<ScanxScheme> candidates = sameAmc.Count > 0 ? sameAmc : schemes;

        if (candidates.FirstOrDefault(s => Key(s.Name) == key) is { } exact)
            return exact;

        // Spelling variants only within the fund house: across AMCs, near-identical names are different funds.
        if (sameAmc.Count == 0)
            return null;

        // Spellings may differ, but series numbers and maturity years tell funds apart: "Series III" and "Series IV",
        // "SDL Sep 2026" and "SDL Sep 2027".
        string markers = Markers(schemeName);
        (ScanxScheme Scheme, double Score)? best = null;
        foreach (ScanxScheme scheme in sameAmc.Where(s => Markers(s.Name) == markers))
        {
            double score = Similarity(key, Key(scheme.Name));
            if (best is null || score > best.Value.Score)
                best = (scheme, score);
        }

        return best is { Score: >= MinSimilarity } match ? match.Scheme : null;
    }

    /// <summary>The name's distinctive words run together: "DSP Large &amp; Mid Cap Fund - Direct Plan" gives "dsplargemidcap".</summary>
    public static string Key(string name) => string.Concat(Words(name));

    private static List<string> Words(string name)
    {
        StringBuilder words = new(name.Length);
        int depth = 0;
        foreach (char c in FundOfFunds().Replace(name.ToLowerInvariant(), "fof"))
        {
            if (c == '(')
                depth++;
            else if (c == ')')
                depth = Math.Max(0, depth - 1);
            // "(formerly ...)" and spelled-out acronyms aren't part of the name.
            else if (depth > 0 || c is '.' or '\'')
                continue;
            else
                words.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return [.. words.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Filler.Contains(w))];
    }

    /// <summary>The numbers and roman numerals in a name, in order: "Series IV", "90:10 Nov 2026" give "iv", "90 10 2026".</summary>
    private static string Markers(string name)
    {
        List<string> words = Words(name);
        IEnumerable<string> numbers = Digits().Matches(string.Concat(words)).Select(m => m.Value);
        return string.Join(' ', words.Where(w => RomanNumeral().IsMatch(w)).Concat(numbers));
    }

    [GeneratedRegex(@"\bfunds?\s+of\s+funds?\b")]
    private static partial Regex FundOfFunds();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex("^[ivxl]{1,6}$")]
    private static partial Regex RomanNumeral();

    private static List<string> AmcWords(string amc) => Words(amc.Replace("Mutual Fund", "", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The longest ScanX fund house whose words start AMFI's renamed one (AMFI's "Zerodha CASE" is ScanX's "Zerodha"),
    /// or null.
    /// </summary>
    private static List<string>? ScanxAmc(List<string> amc, IReadOnlyList<ScanxScheme> schemes) =>
        schemes.Select(s => s.Amc).Distinct(StringComparer.OrdinalIgnoreCase).Select(AmcWords)
            .Where(a => a.Count > 0 && a.Count < amc.Count && amc.Take(a.Count).SequenceEqual(a))
            .MaxBy(a => a.Count);

    /// <summary>Dice coefficient of the two keys' letter pairs.</summary>
    private static double Similarity(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2)
            return a == b ? 1 : 0;

        Dictionary<string, int> pairs = [];
        for (int i = 0; i < a.Length - 1; i++)
            pairs[a.Substring(i, 2)] = pairs.GetValueOrDefault(a.Substring(i, 2)) + 1;

        int common = 0;
        for (int i = 0; i < b.Length - 1; i++)
        {
            string pair = b.Substring(i, 2);
            if (pairs.GetValueOrDefault(pair) > 0)
            {
                pairs[pair]--;
                common++;
            }
        }

        return 2.0 * common / (a.Length - 1 + b.Length - 1);
    }
}

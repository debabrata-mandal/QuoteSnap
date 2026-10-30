using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Nav;

/// <param name="Partial">Not every word of the query matched; these are the closest schemes.</param>
public sealed record FundSearchResult(IReadOnlyList<MutualFundNav> Funds, bool Partial);

/// <summary>
/// Finds schemes from loosely typed names. Word order, case, punctuation, "&amp;" versus "and", joined or split words
/// ("midcap" / "mid cap", "nifty50" / "nifty 50"), partial words ("icici pru"), small typos ("prudentail"),
/// fund house initials ("absl"), ISINs and scheme codes all match.
/// </summary>
public sealed partial class FundSearchIndex
{
    private const int MaxJoinedWords = 3;
    private const int MinFuzzyLength = 4;

    private static readonly HashSet<string> StopWords =
        ["fund", "funds", "the", "of", "and", "plan", "scheme", "schemes", "option", "options", "mutual", "mf", "direct"];

    private static readonly Dictionary<string, string> Synonyms = new()
    {
        ["dividend"] = "idcw",
        ["div"] = "idcw",
    };

    private readonly Entry[] _entries;
    private readonly Dictionary<int, Entry> _byCode;
    private readonly DateOnly _currentSince;

    private FundSearchIndex(Entry[] entries)
    {
        _entries = entries;
        _byCode = entries.ToDictionary(e => e.Nav.SchemeCode);
        // Matured and wound-up schemes stay in AMFI's file with NAVs that are years old.
        _currentSince = entries.Length == 0 ? DateOnly.MinValue : entries.Max(e => e.Nav.NavDate).AddDays(-30);
    }

    public int Count => _entries.Length;

    public static FundSearchIndex Build(IEnumerable<MutualFundNav> navs) => new([.. navs.Select(Entry.Create)]);

    public FundSearchResult Search(string query, int limit)
    {
        string trimmed = query.Trim();
        if (SchemeCodePattern().IsMatch(trimmed)
            && _byCode.TryGetValue(int.Parse(trimmed, CultureInfo.InvariantCulture), out Entry? byCode))
        {
            return new FundSearchResult([byCode.Nav], false);
        }

        string upper = trimmed.ToUpperInvariant();
        if (IsinPattern().IsMatch(upper))
        {
            return new FundSearchResult(
                [.. _entries.Where(e => e.Nav.Isin == upper || e.Nav.IsinReinvestment == upper).Select(e => e.Nav).Take(limit)],
                false);
        }

        string[] words = [.. Words(trimmed).Select(w => Synonyms.GetValueOrDefault(w, w))];
        if (words.Length == 0)
            return new FundSearchResult([], false);

        List<(Entry Entry, EntryMatch Match)> candidates = [];
        foreach (Entry entry in _entries)
        {
            EntryMatch match = entry.Match(words);
            if (match.MatchedWords > 0)
                candidates.Add((entry, match));
        }

        List<(Entry Entry, EntryMatch Match)> results = [.. candidates.Where(c => c.Match.MatchedWords == words.Length)];
        bool partial = false;
        if (results.Count == 0)
        {
            // Renamed schemes ("mirae emerging bluechip") still show the fund house's closest schemes.
            List<(Entry Entry, EntryMatch Match)> named = [.. candidates.Where(c => c.Match.MatchedNameWords > 0)];
            int best = named.Count == 0 ? 0 : named.Max(c => c.Match.MatchedWords);
            results = [.. named.Where(c => c.Match.MatchedWords == best)];
            partial = results.Count > 0;
        }

        return new FundSearchResult(
            [.. results
                .OrderByDescending(r => r.Match.MatchedWords)
                .ThenByDescending(r => r.Entry.Nav.NavDate >= _currentSince)
                .ThenByDescending(r => r.Match.Score)
                .ThenByDescending(r => r.Entry.IsGrowth)
                .ThenBy(r => r.Entry.Nav.SchemeName.Length)
                .ThenBy(r => r.Entry.Nav.SchemeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Entry.Nav.SchemeCode)
                .Take(limit)
                .Select(r => r.Entry.Nav)],
            partial);
    }

    /// <summary>Lower-cased words without punctuation or <see cref="StopWords"/>; "&amp;" counts as "and".</summary>
    internal static List<string> Words(string? text)
    {
        StringBuilder normalized = new((text ?? "").Length);
        foreach (char c in (text ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
                normalized.Append(c);
            else if (c is not ('\'' or '’'))
                normalized.Append(' ');
        }

        return [.. normalized.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w))];
    }

    /// <summary>Optimal string alignment distance, giving up once it exceeds <paramref name="max"/>.</summary>
    internal static int EditDistance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
            return max + 1;

        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            int rowMin = int.MaxValue;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int value = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, d[i - 2, j - 2] + 1);
                d[i, j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > max)
                return max + 1;
        }

        return d[a.Length, b.Length];
    }

    [GeneratedRegex(@"^\d{4,7}$")]
    private static partial Regex SchemeCodePattern();

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{9}[0-9]$")]
    private static partial Regex IsinPattern();

    /// <param name="MatchedWords">Query words found in the scheme, counting each word of a joined match.</param>
    /// <param name="MatchedNameWords">Of those, the ones found in the scheme name rather than the fund house, option or ISIN.</param>
    private readonly record struct EntryMatch(int MatchedWords, int MatchedNameWords, double Score);

    /// <param name="Position">Index of the term's first word in the scheme name; -1 for other fields.</param>
    private readonly record struct Term(string Text, int Position)
    {
        public bool InName => Position >= 0;
    }

    private readonly record struct TermMatch(double Score, int Position, bool InName);

    private sealed class Entry
    {
        private readonly Term[] _terms;
        private readonly int _nameWordCount;

        private Entry(MutualFundNav nav, Term[] terms, int nameWordCount, bool isGrowth)
        {
            Nav = nav;
            _terms = terms;
            _nameWordCount = nameWordCount;
            IsGrowth = isGrowth;
        }

        public MutualFundNav Nav { get; }

        public bool IsGrowth { get; }

        public static Entry Create(MutualFundNav nav)
        {
            List<string> name = Words(nav.SchemeName);
            List<Term> terms = [.. Joined(name, inName: true)];

            List<string> house = Words(nav.FundHouse);
            terms.AddRange(Joined(house, inName: false));
            string[] houseWords = [.. (nav.FundHouse ?? "").ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w is not ("mutual" or "fund"))];
            if (houseWords.Length >= 3)
                terms.Add(new Term(string.Concat(houseWords.Select(w => w[0])), -1));

            List<string> option = Words(nav.Option);
            terms.AddRange(option.Select(w => new Term(w, -1)));
            bool distribution = nav.Option?.Contains("income distribution", StringComparison.OrdinalIgnoreCase) == true;
            if (distribution)
                terms.Add(new Term("idcw", -1));

            foreach (string? isin in new[] { nav.Isin, nav.IsinReinvestment })
            {
                if (!string.IsNullOrEmpty(isin))
                    terms.Add(new Term(isin.ToLowerInvariant(), -1));
            }

            bool isGrowth = option.Contains("growth") && !option.Contains("idcw") && !distribution;
            return new Entry(nav, [.. terms.DistinctBy(t => (t.Text, t.InName))], name.Count, isGrowth);
        }

        public EntryMatch Match(string[] words)
        {
            int matched = 0;
            int matchedInName = 0;
            double score = 0;
            int lastPosition = -1;
            bool inOrder = true;
            bool startsAtFirstWord = false;

            for (int i = 0; i < words.Length;)
            {
                TermMatch best = default;
                int span = 1;
                for (int n = Math.Min(MaxJoinedWords, words.Length - i); n >= 1; n--)
                {
                    string query = n == 1 ? words[i] : string.Concat(words[i..(i + n)]);
                    TermMatch candidate = MatchTerm(query, allowTypos: n == 1);
                    if (candidate.Score > 0)
                    {
                        best = candidate;
                        span = n;
                        break;
                    }
                }

                if (best.Score > 0)
                {
                    matched += span;
                    score += best.Score * span;
                    if (best.InName)
                    {
                        matchedInName += span;
                        if (best.Position < lastPosition)
                            inOrder = false;
                        if (i == 0 && best.Position == 0)
                            startsAtFirstWord = true;
                        lastPosition = best.Position;
                    }
                }

                i += span;
            }

            if (matchedInName > 1 && inOrder)
                score += 2;
            if (startsAtFirstWord)
                score += 3;
            if (matchedInName >= _nameWordCount)
                score += 3;
            return new EntryMatch(matched, matchedInName, score);
        }

        private TermMatch MatchTerm(string query, bool allowTypos)
        {
            TermMatch best = default;
            foreach (Term term in _terms)
            {
                double score = 0;
                if (term.Text == query)
                    score = term.InName ? 10 : 5;
                else if (query.Length >= 2 && term.Text.StartsWith(query, StringComparison.Ordinal))
                    score = (term.InName ? 6 : 3) + (term.InName ? 3 : 1) * query.Length / (double)term.Text.Length;

                if (score > best.Score)
                    best = new TermMatch(score, term.Position, term.InName);
            }

            // "250" is not a typo of "500".
            if (best.Score > 0 || !allowTypos || query.Length < MinFuzzyLength || query.Any(char.IsDigit))
                return best;

            int maxEdits = query.Length >= 8 ? 2 : 1;
            foreach (Term term in _terms)
            {
                if (IsTypoOf(query, term.Text, maxEdits))
                {
                    double score = term.InName ? 4 : 2;
                    if (score > best.Score)
                        best = new TermMatch(score, term.Position, term.InName);
                }
            }

            return best;
        }

        /// <summary>Close to the whole term, or to its start when the query is a partial word.</summary>
        private static bool IsTypoOf(string query, string term, int maxEdits)
        {
            // Typos rarely hit both of the first two letters; skipping those terms keeps a search to a few milliseconds.
            if (term.Length < 2 || (term[0] != query[0] && term[1] != query[1]))
                return false;

            if (EditDistance(query, term, maxEdits) <= maxEdits)
                return true;

            for (int length = query.Length - 1; length <= query.Length + 1; length++)
            {
                if (length >= MinFuzzyLength && length < term.Length
                    && EditDistance(query, term[..length], maxEdits) <= maxEdits)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Each word, plus runs of up to <see cref="MaxJoinedWords"/> words written together.</summary>
        private static IEnumerable<Term> Joined(List<string> words, bool inName)
        {
            for (int start = 0; start < words.Count; start++)
            {
                for (int n = 1; n <= MaxJoinedWords && start + n <= words.Count; n++)
                    yield return new Term(string.Concat(words.Skip(start).Take(n)), inName ? start : -1);
            }
        }
    }
}

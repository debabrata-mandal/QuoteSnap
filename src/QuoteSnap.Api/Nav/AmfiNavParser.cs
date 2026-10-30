using System.Globalization;
using System.Text.RegularExpressions;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Nav;

public sealed record AmfiNavFile(IReadOnlyList<MutualFundNav> Navs, int SkippedRows);

/// <summary>
/// Parses AMFI's NAVAll.txt: a header row, then semicolon-separated scheme rows grouped under category headings
/// ("Open Ended Schemes(...)") and fund house headings ("Axis Mutual Fund").
/// </summary>
public static partial class AmfiNavParser
{
    public static AmfiNavFile Parse(string text)
    {
        string[] lines = text.Split('\n');
        int headerIndex = Array.FindIndex(lines, l => l.Contains("Scheme Code", StringComparison.OrdinalIgnoreCase));
        if (headerIndex < 0)
            throw new FormatException("The NAV file has no 'Scheme Code' header row.");

        Columns columns = Columns.FromHeader(lines[headerIndex]);
        Dictionary<int, MutualFundNav> bySchemeCode = [];
        string? category = null;
        string? fundHouse = null;
        int skipped = 0;

        foreach (string raw in lines.Skip(headerIndex + 1))
        {
            string line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (!line.Contains(';'))
            {
                if (CategoryHeading().IsMatch(line))
                {
                    category = line;
                    fundHouse = null;
                }
                else
                {
                    fundHouse = line;
                }
                continue;
            }

            if (columns.TryParseRow(line.Split(';'), category, fundHouse) is { } nav)
                bySchemeCode[nav.SchemeCode] = nav;
            else
                skipped++;
        }

        return new AmfiNavFile([.. bySchemeCode.Values], skipped);
    }

    [GeneratedRegex(@"Schemes\s*\(")]
    private static partial Regex CategoryHeading();

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{9}[0-9]$")]
    private static partial Regex IsinPattern();

    private sealed record Columns(
        int SchemeCode, int Isin, int IsinReinvestment, int SchemeName, int Plan, int Option, int Nav, int Date)
    {
        // Older layouts of the file have no Plan and Option columns.
        public static Columns FromHeader(string header)
        {
            string[] names = [.. header.Split(';').Select(n => n.Trim())];
            int Find(Func<string, bool> match) => Array.FindIndex(names, n => match(n));
            int Required(string name) => Find(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is >= 0 and int i
                ? i
                : throw new FormatException($"The NAV file header has no '{name}' column.");

            return new Columns(
                Required("Scheme Code"),
                Find(n => n.Contains("Growth", StringComparison.OrdinalIgnoreCase)),
                Find(n => n.Contains("Reinvestment", StringComparison.OrdinalIgnoreCase)),
                Required("Scheme Name"),
                Find(n => n.Equals("Plan", StringComparison.OrdinalIgnoreCase)),
                Find(n => n.Equals("Option", StringComparison.OrdinalIgnoreCase)),
                Required("Net Asset Value"),
                Required("Date"));
        }

        public MutualFundNav? TryParseRow(string[] fields, string? category, string? fundHouse)
        {
            string? Field(int index) => index >= 0 && index < fields.Length && fields[index].Trim() is { Length: > 0 } value
                ? value
                : null;

            if (!int.TryParse(Field(SchemeCode), NumberStyles.None, CultureInfo.InvariantCulture, out int schemeCode)
                || Field(SchemeName) is not { } schemeName
                || !decimal.TryParse(Field(Nav), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal nav)
                || !DateOnly.TryParseExact(Field(Date), "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly navDate))
            {
                return null;
            }

            return new MutualFundNav
            {
                SchemeCode = schemeCode,
                Isin = AsIsin(Field(Isin)),
                IsinReinvestment = AsIsin(Field(IsinReinvestment)),
                SchemeName = schemeName,
                FundHouse = fundHouse,
                Category = category,
                Plan = Field(Plan),
                Option = Field(Option),
                Nav = nav,
                NavDate = navDate,
            };
        }

        // The ISIN columns also hold placeholders such as "-" and "Redeemed".
        private static string? AsIsin(string? value) =>
            value is not null && IsinPattern().IsMatch(value) ? value : null;
    }
}

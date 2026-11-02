using System.Text.RegularExpressions;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Holdings;

/// <summary>
/// Reads Fyers' symbol master CSVs (public.fyers.in/sym_details/NSE_CM.csv and BSE_CM.csv), which pair each instrument's
/// ISIN with the symbol the Fyers quotes API takes, e.g. INE002A01018 → NSE:RELIANCE-EQ.
/// </summary>
/// <remarks>
/// The files have no header and Fyers has added columns before, so the ISIN and symbol are found by their shape rather
/// than their position. The name has always been the second column.
/// </remarks>
public static partial class FyersSymbolMaster
{
    private const int MaxNameLength = 300;

    /// <summary>Each ISIN in the file with its Fyers symbol and name; when an ISIN appears twice, the first line wins.</summary>
    /// <exception cref="FormatException">No line has both an ISIN and a Fyers symbol.</exception>
    public static Dictionary<string, FyersSymbol> Parse(string csv)
    {
        Dictionary<string, FyersSymbol> symbols = new(StringComparer.Ordinal);
        foreach (string line in csv.Split('\n'))
        {
            string[] fields = line.TrimEnd('\r').Split(',');
            string? isin = fields.FirstOrDefault(f => IsinPattern().IsMatch(f));
            string? symbol = fields.FirstOrDefault(f => SymbolPattern().IsMatch(f));
            if (isin is null || symbol is null || symbols.ContainsKey(isin))
                continue;

            string name = fields[1].Trim();
            symbols.Add(isin, new FyersSymbol
            {
                Isin = isin,
                Symbol = symbol,
                Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name,
            });
        }

        return symbols.Count > 0
            ? symbols
            : throw new FormatException("The Fyers symbol master has no lines with an ISIN and a symbol.");
    }

    /// <summary>NSE's symbol for each ISIN, or BSE's when the instrument isn't listed on NSE.</summary>
    public static List<FyersSymbol> Combine(IReadOnlyDictionary<string, FyersSymbol> nse, IReadOnlyDictionary<string, FyersSymbol> bse) =>
        [.. nse.Values, .. bse.Values.Where(s => !nse.ContainsKey(s.Isin))];

    [GeneratedRegex("^IN[A-Z0-9]{10}$")]
    private static partial Regex IsinPattern();

    [GeneratedRegex("^(NSE|BSE):[^ ]+$")]
    private static partial Regex SymbolPattern();
}

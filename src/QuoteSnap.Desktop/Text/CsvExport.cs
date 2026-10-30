using System.Globalization;
using System.Text;
using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Text;

public static class CsvExport
{
    public static string Quotes(IEnumerable<QuoteResultDto> results) => Write(results,
    [
        ("Symbol", r => r.Symbol),
        ("Status", r => r.Status),
        ("LTP", r => r.Ltp),
        ("Change", r => r.Change),
        ("Change %", r => r.ChangePercent),
        ("Open", r => r.Open),
        ("High", r => r.High),
        ("Low", r => r.Low),
        ("Previous close", r => r.PreviousClose),
        ("Volume", r => r.Volume),
        ("Exchange time", r => r.ExchangeTime),
        ("Fetched at", r => r.FetchedAt),
        ("Error", r => r.Error),
    ]);

    public static string Navs(IEnumerable<FundNav> funds) => Write(funds,
    [
        ("Scheme code", f => f.SchemeCode),
        ("ISIN", f => f.Isin),
        ("ISIN reinvestment", f => f.IsinReinvestment),
        ("Scheme name", f => f.SchemeName),
        ("Fund house", f => f.FundHouse),
        ("Category", f => f.Category),
        ("Plan", f => f.Plan),
        ("Option", f => f.Option),
        ("NAV", f => f.Nav),
        ("NAV date", f => f.NavDate),
    ]);

    public static string Holdings(FundHoldings fund) => Write(fund.Holdings,
    [
        ("Scheme code", _ => fund.SchemeCode),
        ("Scheme name", _ => fund.SchemeName),
        ("Month", _ => fund.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture)),
        ("Holding", h => h.Name),
        ("Symbol", h => h.Symbol),
        ("ISIN", h => h.Isin),
        ("Asset type", h => h.AssetType),
        ("Sector", h => h.Sector),
        ("Weight %", h => h.Percent),
        ("Previous month weight %", h => h.PreviousPercent),
        ("Shares", h => h.Shares),
    ]);

    public static string Write<T>(IEnumerable<T> rows, IReadOnlyList<(string Header, Func<T, object?> Value)> columns)
    {
        StringBuilder csv = new();
        csv.AppendJoin(',', columns.Select(c => Escape(c.Header))).Append("\r\n");
        foreach (T row in rows)
            csv.AppendJoin(',', columns.Select(c => Format(c.Value(row)))).Append("\r\n");
        return csv.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTimeOffset time => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable number when value is not Enum => number.ToString(null, CultureInfo.InvariantCulture),
        _ => Escape(value.ToString() ?? ""),
    };

    private static string Escape(string text)
    {
        // Spreadsheets run cells that start with these as formulas.
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
            text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}

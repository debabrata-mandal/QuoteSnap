using System.Globalization;
using System.Text.Json;

namespace QuoteSnap.Api.Holdings;

/// <summary>A Direct-Growth scheme on ScanX's AMC list. ScanX lists no other plans; their portfolios are the same.</summary>
public sealed record ScanxScheme(string Amc, int PlanId, string Name);

/// <summary>One instrument in a scheme's portfolio, with its weight in each month ScanX has.</summary>
/// <param name="AssetType">ScanX's grouping, e.g. "Equity", "Futures", "TREPS".</param>
/// <param name="Symbol">The exchange symbol; null for instruments that aren't listed, such as bonds and TREPS.</param>
/// <param name="Months">Newest first.</param>
public sealed record ScanxHolding(
    string AssetType,
    string Name,
    string? Symbol,
    string? Isin,
    string? Sector,
    IReadOnlyList<ScanxHoldingMonth> Months);

/// <param name="Month">The first day of the month.</param>
public sealed record ScanxHoldingMonth(DateOnly Month, decimal Percent, decimal Shares);

/// <param name="TotalPages">Zero when ScanX has no holdings for the scheme.</param>
public sealed record ScanxHoldingsPage(IReadOnlyList<ScanxHolding> Holdings, int TotalPages);

/// <summary>Reads ScanX's amclist_v2 and mfpastholdings_v2 responses.</summary>
public static class ScanxJson
{
    /// <exception cref="FormatException">Not an AMC list.</exception>
    public static IReadOnlyList<ScanxScheme> ParseSchemes(string json)
    {
        using JsonDocument document = Parse(json);
        JsonElement data = DataOf(document.RootElement);
        if (data.ValueKind != JsonValueKind.Array)
            throw new FormatException("ScanX's AMC list has no 'data' array.");

        List<ScanxScheme> schemes = [];
        foreach (JsonElement amc in data.EnumerateArray())
        {
            if (Text(amc, "amc") is not { } amcName || !amc.TryGetProperty("scheme", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement scheme in list.EnumerateArray())
            {
                if (Text(scheme, "name") is { } name
                    && int.TryParse(Text(scheme, "plan_id"), NumberStyles.None, CultureInfo.InvariantCulture, out int planId))
                {
                    schemes.Add(new ScanxScheme(amcName, planId, name));
                }
            }
        }

        if (schemes.Count == 0)
            throw new FormatException("ScanX's AMC list has no schemes.");
        return schemes;
    }

    /// <exception cref="FormatException">Not a holdings response, or ScanX reported an error.</exception>
    public static ScanxHoldingsPage ParseHoldingsPage(string json)
    {
        using JsonDocument document = Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("code", out JsonElement code) && code.TryGetInt32(out int value) && value != 0)
        {
            string remarks = Text(root, "remarks") ?? "";
            // An unknown plan, or one ScanX has no portfolio for yet.
            if (remarks.Contains("no record", StringComparison.OrdinalIgnoreCase))
                return new ScanxHoldingsPage([], 0);
            throw new FormatException($"ScanX answered with code {value}: {remarks}");
        }

        if (!root.TryGetProperty("headers", out JsonElement headers) || headers.ValueKind != JsonValueKind.Array)
            throw new FormatException("ScanX's holdings response has no 'headers'.");
        Columns columns = Columns.From(headers);

        int totalPages = root.TryGetProperty("meta", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("totalpages", out JsonElement pages) && pages.TryGetInt32(out int count)
            ? count
            : 1;

        List<ScanxHolding> holdings = [];
        JsonElement data = DataOf(root);
        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty group in data.EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement row in group.Value.EnumerateArray())
                {
                    if (row.ValueKind == JsonValueKind.Array && ReadRow(group.Name, row, columns) is { } holding)
                        holdings.Add(holding);
                }
            }
        }

        return new ScanxHoldingsPage(holdings, totalPages);
    }

    private static ScanxHolding? ReadRow(string assetType, JsonElement row, Columns columns)
    {
        string? name = Cell(row, columns.Display) ?? Cell(row, columns.Symbol);
        if (name is null)
            return null;

        string[] months = (Cell(row, columns.Date) ?? "").Split('|');
        string[] percents = (Cell(row, columns.Percent) ?? "").Split('|');
        string[] shares = (Cell(row, columns.Shares) ?? "").Split('|');
        List<ScanxHoldingMonth> values = [];
        for (int i = 0; i < months.Length; i++)
        {
            if (DateOnly.TryParseExact(months[i], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly month)
                && i < percents.Length && Number(percents[i]) is { } percent)
            {
                values.Add(new ScanxHoldingMonth(month, percent, i < shares.Length ? Number(shares[i]) ?? 0 : 0));
            }
        }

        // Listed instruments have an exchange; for the rest ScanX repeats the name as the symbol.
        string? symbol = Cell(row, columns.Exchange) is null ? null : Cell(row, columns.Symbol);
        return new ScanxHolding(
            assetType,
            name,
            symbol,
            Cell(row, columns.Isin),
            Cell(row, columns.Sector),
            [.. values.OrderByDescending(v => v.Month)]);
    }

    private static decimal? Number(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"ScanX's response isn't JSON: {ex.Message}", ex);
        }
    }

    private static JsonElement DataOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out JsonElement data))
            throw new FormatException("ScanX's response has no 'data'.");
        return data;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) ? TextOf(value) : null;

    private static string? Cell(JsonElement row, int index) =>
        index >= 0 && index < row.GetArrayLength() ? TextOf(row[index]) : null;

    private static string? TextOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() is { Length: > 0 } text ? text.Trim() : null,
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    private sealed record Columns(int Symbol, int Display, int Exchange, int Isin, int Shares, int Percent, int Date, int Sector)
    {
        public static Columns From(JsonElement headers)
        {
            List<string?> names = [.. headers.EnumerateArray().Select(h => h.ValueKind == JsonValueKind.String ? h.GetString() : null)];
            int percent = names.IndexOf("assetPerc");
            int date = names.IndexOf("date");
            if (percent < 0 || date < 0)
                throw new FormatException("ScanX's holdings headers have no 'assetPerc' or 'date'.");
            return new Columns(
                names.IndexOf("sym"), names.IndexOf("disp"), names.IndexOf("exch"), names.IndexOf("isin"),
                names.IndexOf("shares"), percent, date, names.IndexOf("sector"));
        }
    }
}

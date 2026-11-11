using System.Text.Json;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Domain;

namespace QuoteSnap.Api.Fyers;

public sealed class FyersQuoteProvider(HttpClient http, IOptions<FyersOptions> options) : IQuoteProvider
{
    private readonly FyersOptions _options = options.Value;

    public async Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(string appId, string accessToken, IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
            return [];

        string query = string.Join(',', symbols.Select(Uri.EscapeDataString));
        Uri url = new($"{_options.DataBaseUrl.TrimEnd('/')}/quotes?symbols={query}");
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{accessToken}");

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        using JsonDocument document = await FyersJson.ReadOkAsync(response, cancellationToken);

        Dictionary<string, ProviderQuote> bySymbol = new(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("d", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (FyersJson.GetString(item, "n") is { } name)
                    bySymbol[name] = Parse(name, item);
            }
        }

        return symbols
            .Select(symbol => bySymbol.TryGetValue(symbol, out ProviderQuote? quote)
                ? quote with { Symbol = symbol }
                : ProviderQuote.Failure(symbol, "Fyers returned no data for this symbol."))
            .ToList();
    }

    private const int NseExchange = 10;
    private const int CapitalMarketSegment = 10;

    public async Task<string?> GetMarketStatusAsync(string appId, string accessToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri($"{_options.DataBaseUrl.TrimEnd('/')}/marketStatus"));
        request.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{accessToken}");

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        using JsonDocument document = await FyersJson.ReadOkAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("marketStatus", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            return null;

        return rows.EnumerateArray()
            .Where(row => FyersJson.GetInt(row, "exchange") == NseExchange && FyersJson.GetInt(row, "segment") == CapitalMarketSegment && FyersJson.GetString(row, "market_type") == "NORMAL")
            .Select(row => FyersJson.GetString(row, "status"))
            .FirstOrDefault();
    }

    private static ProviderQuote Parse(string symbol, JsonElement item)
    {
        JsonElement values = item.TryGetProperty("v", out JsonElement v) ? v : default;

        if (FyersJson.GetString(item, "s") is { } status && status != "ok")
        {
            string error = FyersJson.GetString(values, "errmsg")
                ?? FyersJson.GetString(item, "message")
                ?? "Fyers returned an error for this symbol.";
            return ProviderQuote.Failure(symbol, error);
        }

        decimal? ltp = FyersJson.GetDecimal(values, "lp");
        if (ltp is null)
            return ProviderQuote.Failure(symbol, "Fyers returned no last traded price; the stock may be suspended or not traded yet. Check the symbol, or exclude it for today.");

        return new ProviderQuote(symbol)
        {
            Ltp = ltp,
            Change = FyersJson.GetDecimal(values, "ch"),
            ChangePercent = FyersJson.GetDecimal(values, "chp"),
            Open = FyersJson.GetDecimal(values, "open_price"),
            High = FyersJson.GetDecimal(values, "high_price"),
            Low = FyersJson.GetDecimal(values, "low_price"),
            PreviousClose = FyersJson.GetDecimal(values, "prev_close_price"),
            Volume = FyersJson.GetLong(values, "volume"),
            ExchangeTime = FyersJson.GetUnixTime(values, "tt"),
        };
    }
}

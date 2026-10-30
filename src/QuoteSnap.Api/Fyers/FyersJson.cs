using System.Globalization;
using System.Text.Json;

namespace QuoteSnap.Api.Fyers;

/// <summary>Lenient readers for Fyers responses, which mix numbers and numeric strings.</summary>
internal static class FyersJson
{
    /// <summary>Returns the parsed body, or throws a mapped <see cref="FyersApiException"/> for error responses.</summary>
    public static async Task<JsonDocument> ReadOkAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
        }

        JsonElement? root = document?.RootElement;
        string? status = root is { } r ? GetString(r, "s") : null;
        bool ok = response.IsSuccessStatusCode && document is not null && (status is null || status == "ok");
        if (ok)
            return document!;

        int? code = root is { } e ? GetInt(e, "code") : null;
        string? message = root is { } m ? GetString(m, "message") : null;
        document?.Dispose();
        throw FyersApiException.Create((int)response.StatusCode, code, message);
    }

    public static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? GetInt(JsonElement element, string name) =>
        GetDecimal(element, name) is { } d && d == Math.Truncate(d) && d is >= int.MinValue and <= int.MaxValue
            ? (int)d
            : null;

    public static long? GetLong(JsonElement element, string name) =>
        GetDecimal(element, name) is { } d && d is >= long.MinValue and <= long.MaxValue ? (long)d : null;

    public static decimal? GetDecimal(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out decimal d) => d,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d) => d,
            _ => null,
        };
    }

    public static DateTimeOffset? GetUnixTime(JsonElement element, string name) =>
        GetLong(element, name) is { } seconds and > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}

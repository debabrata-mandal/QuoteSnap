using System.Net;
using System.Text.Json;

namespace QuoteSnap.Client;

/// <summary>The QuoteSnap service returned a non-success status. Title and Detail come from its ProblemDetails body.</summary>
public sealed class QuoteSnapApiException(HttpStatusCode statusCode, string? title, string? detail)
    : Exception(detail ?? title ?? $"QuoteSnap returned HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string? Title { get; } = title;

    public string? Detail { get; } = detail;

    internal static async Task<QuoteSnapApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            return new QuoteSnapApiException(response.StatusCode, Read(root, "title"), Read(root, "detail"));
        }
        catch (JsonException)
        {
            string text = body.Length > 300 ? body[..300] : body;
            return new QuoteSnapApiException(response.StatusCode, response.ReasonPhrase, text.Length > 0 ? text : null);
        }
    }

    private static string? Read(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

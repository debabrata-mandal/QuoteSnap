using System.Net;
using System.Net.Http;
using QuoteSnap.Client;

namespace QuoteSnap.Desktop.Services;

public static class ErrorText
{
    public static string Describe(Exception ex, Uri baseAddress) => ex switch
    {
        QuoteSnapApiException { StatusCode: HttpStatusCode.Unauthorized } =>
            "The API key was rejected. Check it on the Settings tab.",
        QuoteSnapApiException api =>
            $"{(int)api.StatusCode} {api.Title ?? api.StatusCode.ToString()}{(api.Detail is { } detail && detail != api.Title ? $": {detail}" : "")}",
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } =>
            $"Can't reach the QuoteSnap API at {baseAddress}. Is it running?",
        HttpRequestException =>
            $"Can't reach the QuoteSnap API at {baseAddress}. Is it running? ({ex.Message})",
        TaskCanceledException =>
            "The QuoteSnap API didn't respond in time.",
        _ => ex.Message,
    };
}

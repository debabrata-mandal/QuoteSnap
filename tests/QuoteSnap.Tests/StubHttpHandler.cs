using System.Net;
using System.Text;

namespace QuoteSnap.Tests;

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}

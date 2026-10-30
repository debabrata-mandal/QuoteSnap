using System.Net.Http;
using QuoteSnap.Desktop.Services;

namespace QuoteSnap.Desktop.Tests;

public sealed class ErrorTextTests
{
    private static readonly Uri Api = new("http://127.0.0.1:8080/");

    [Fact]
    public void A_refused_connection_reads_without_the_socket_detail()
    {
        HttpRequestException ex = new(HttpRequestError.ConnectionError, "No connection could be made");

        Assert.Equal("Can't reach the QuoteSnap API at http://127.0.0.1:8080/. Is it running?", ErrorText.Describe(ex, Api));
    }

    [Fact]
    public void Other_transport_errors_keep_their_detail()
    {
        HttpRequestException ex = new(HttpRequestError.SecureConnectionError, "The SSL connection could not be established");

        Assert.EndsWith("Is it running? (The SSL connection could not be established)", ErrorText.Describe(ex, Api));
    }
}

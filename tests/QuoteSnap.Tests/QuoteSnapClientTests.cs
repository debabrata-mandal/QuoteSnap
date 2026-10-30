using System.Net;
using Microsoft.Extensions.Options;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Tests;

public sealed class QuoteSnapClientTests
{
    private static readonly Guid RequestId = Guid.Parse("0199f2a8-0000-7000-8000-000000000001");

    [Fact]
    public async Task WaitForCompletion_polls_until_the_request_finishes()
    {
        Queue<string> statuses = new(["Running", "Running", "Completed"]);
        StubHttpHandler handler = new(_ =>
        {
            string status = statuses.Dequeue();
            HttpResponseMessage response = StubHttpHandler.Json(DetailsJson(status));
            if (status == "Running")
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return response;
        });
        List<QuoteRequestStatus> reported = new();

        QuoteRequestDetails details = await CreateClient(handler).WaitForCompletionAsync(
            RequestId, new SynchronousProgress(d => reported.Add(d.Status)));

        Assert.Equal(QuoteRequestStatus.Completed, details.Status);
        Assert.Equal([QuoteRequestStatus.Running, QuoteRequestStatus.Running, QuoteRequestStatus.Completed], reported);
        Assert.All(handler.Requests, r => Assert.Equal("test-key", r.Headers.GetValues(QuoteSnapHeaders.ApiKey).Single()));
    }

    [Fact]
    public async Task Problem_responses_become_QuoteSnapApiException()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json(
            """{ "title": "Fyers re-authentication required", "detail": "Run the login tool.", "status": 503 }""",
            HttpStatusCode.ServiceUnavailable,
            "application/problem+json"));

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => CreateClient(handler).SubmitAsync(["NSE:SBIN-EQ"]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal("Fyers re-authentication required", ex.Title);
        Assert.Equal("Run the login tool.", ex.Detail);
    }

    [Fact]
    public async Task Admin_calls_send_the_admin_key()
    {
        StubHttpHandler handler = new(_ => StubHttpHandler.Json("""{ "state": "ReloginRequired" }"""));

        FyersSessionInfo session = await CreateClient(handler).GetFyersSessionAsync();

        Assert.Equal(FyersAuthState.ReloginRequired, session.State);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("admin-key", request.Headers.GetValues(QuoteSnapHeaders.AdminKey).Single());
        Assert.False(request.Headers.Contains(QuoteSnapHeaders.ApiKey));
    }

    private static QuoteSnapClient CreateClient(StubHttpHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://quotesnap.test/") },
            Options.Create(new QuoteSnapClientOptions
            {
                ApiKey = "test-key",
                AdminApiKey = "admin-key",
                MinPollInterval = TimeSpan.Zero,
                MaxPollInterval = TimeSpan.FromMilliseconds(10),
            }));

    private static string DetailsJson(string status) => $$"""
        {
          "requestId": "{{RequestId}}", "status": "{{status}}", "createdAt": "2026-10-29T05:00:00+00:00",
          "totalSymbols": 1, "succeededCount": 0, "failedCount": 0, "results": []
        }
        """;

    private sealed class SynchronousProgress(Action<QuoteRequestDetails> report) : IProgress<QuoteRequestDetails>
    {
        public void Report(QuoteRequestDetails value) => report(value);
    }
}

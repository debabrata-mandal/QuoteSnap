using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace QuoteSnap.Api.Holdings;

/// <summary>
/// Calls the JSON API that scanx.trade/insight/mf-holdings loads its AMC list and holdings from, the same requests the
/// page sends from the browser. No login or key is needed.
/// </summary>
public sealed class ScanxClient(HttpClient http, IOptions<HoldingsOptions> options)
{
    /// <exception cref="HttpRequestException">The call failed.</exception>
    /// <exception cref="FormatException">The response isn't an AMC list.</exception>
    public async Task<IReadOnlyList<ScanxScheme>> GetSchemesAsync(CancellationToken cancellationToken) =>
        ScanxJson.ParseSchemes(await http.GetStringAsync("amclist_v2", cancellationToken));

    /// <param name="page">From 1.</param>
    /// <exception cref="HttpRequestException">The call failed.</exception>
    /// <exception cref="FormatException">The response isn't a holdings page, or ScanX reported an error.</exception>
    public async Task<ScanxHoldingsPage> GetHoldingsPageAsync(int planId, int page, CancellationToken cancellationToken)
    {
        HoldingsRequest body = new(new HoldingsQuery(planId, "M", options.Value.PageSize, page));
        using HttpResponseMessage response = await http.PostAsJsonAsync("mfpastholdings_v2", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ScanxJson.ParseHoldingsPage(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private sealed record HoldingsRequest([property: JsonPropertyName("data")] HoldingsQuery Data);

    /// <param name="Timeframe">"M" for month-end portfolios; the page's yearly view uses "Y".</param>
    private sealed record HoldingsQuery(
        [property: JsonPropertyName("planid")] int PlanId,
        [property: JsonPropertyName("timeframe")] string Timeframe,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("page")] int Page);
}

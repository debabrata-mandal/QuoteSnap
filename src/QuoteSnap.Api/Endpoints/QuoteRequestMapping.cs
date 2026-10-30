using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Endpoints;

internal static class QuoteRequestMapping
{
    public static QuoteRequestSummary ToSummary(this QuoteRequest r) => new(
        r.Id, r.Status, r.CreatedAt, r.StartedAt, r.CompletedAt, r.TotalSymbols, r.SucceededCount, r.FailedCount, r.Error);

    public static QuoteRequestDetails ToDetails(this QuoteRequest r) => new(
        r.Id, r.Status, r.CreatedAt, r.StartedAt, r.CompletedAt, r.TotalSymbols, r.SucceededCount, r.FailedCount, r.Error,
        r.Results.OrderBy(x => x.Position).Select(ToDto).ToList());

    private static QuoteResultDto ToDto(QuoteResult x) => new(
        x.Symbol, x.Status, x.Ltp, x.Change, x.ChangePercent, x.Open, x.High, x.Low, x.PreviousClose, x.Volume,
        x.ExchangeTime, x.FetchedAt, x.Error);
}

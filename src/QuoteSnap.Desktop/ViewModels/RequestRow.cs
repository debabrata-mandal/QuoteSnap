using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed record RequestRow(QuoteRequestSummary Summary)
{
    public Guid Id => Summary.RequestId;

    public string CreatedText => Display.When(Summary.CreatedAt);

    public string StatusText => Text.StatusText.Of(Summary.Status);

    /// <summary>The last 8 hex digits: request IDs are version 7 GUIDs, which start with a timestamp, so it's the end that differs.</summary>
    public string ShortId => Display.ShortId(Summary.RequestId);

    public string Duration => Display.Duration(Summary.CompletedAt - Summary.StartedAt);

    public string Result => Summary.Status.IsTerminal()
        ? $"{Summary.SucceededCount} ok, {Summary.FailedCount} failed"
        : $"{Summary.SucceededCount + Summary.FailedCount} of {Summary.TotalSymbols} done";
}

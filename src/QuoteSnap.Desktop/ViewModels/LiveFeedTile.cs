using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>The dashboard's live feed tile: the streaming state and how many stocks are hooked.</summary>
public sealed record LiveFeedTile(StatusLevel Level, string Summary, string Value, string Detail, string? Tip, double CapFraction)
{
    /// <summary>Fyers' Data Socket limit per user, across all its connections.</summary>
    public const int SymbolCap = 100;

    public bool HasValue => Value != Display.Missing;

    public static LiveFeedTile Unknown(string summary, string detail) => new(StatusLevel.Unknown, summary, Display.Missing, detail, null, 0);

    public static LiveFeedTile From(LiveFeedStatus status)
    {
        int count = status.Symbols.Count;
        string value = count > 0 ? count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : Display.Missing;
        string priced = status.QuoteCount < count ? $"{status.QuoteCount:N0} of {count:N0} priced · " : "";
        string ticks = status.LastTickAt is { } at ? $"last tick {Display.WhenInSentence(at)}" : "waiting for ticks";
        string? tip = status.LastError is { } error ? $"Last error {Display.WhenInSentence(status.LastErrorAt)}: {error}" : null;
        double cap = Math.Clamp(count / (double)SymbolCap, 0, 1);
        (StatusLevel level, string summary, string detail) = status.State switch
        {
            LiveFeedState.Connected => (StatusLevel.Good, "Live", priced + ticks),
            LiveFeedState.Connecting => (StatusLevel.Busy, "Connecting", "Connecting to the Fyers Data Socket"),
            LiveFeedState.Reconnecting => (StatusLevel.Warning, "Reconnecting", "Connection dropped · " + ticks),
            LiveFeedState.Error => (StatusLevel.Bad, "Error", status.LastError ?? "Retrying in a minute"),
            LiveFeedState.NoSession => (StatusLevel.Bad, "Login needed", "Streams once you log in to Fyers"),
            LiveFeedState.NoHoldings => (StatusLevel.Warning, "No holdings", "Fetch holdings on the Holdings page"),
            LiveFeedState.Disabled => (StatusLevel.Unknown, "Off", "Turned off in the API settings"),
            _ => (StatusLevel.Unknown, "Starting", "Waiting for the feed to start"),
        };
        return new LiveFeedTile(level, summary, value, detail, tip ?? $"{Display.Count(count, "stock")} of Fyers' {SymbolCap}-symbol limit", cap);
    }
}

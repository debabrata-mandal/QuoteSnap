using QuoteSnap.Contracts;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Controls;

/// <summary>The semantic colour of a pill, icon or chip; each has a strong and a subtle brush in the palettes in Themes/Palettes.</summary>
public enum Tone
{
    Neutral,
    Accent,
    Info,
    Success,
    Warning,
    Danger,
}

public static class Tones
{
    public static Tone Of(object? value) => value switch
    {
        Tone tone => tone,
        StatusLevel.Good => Tone.Success,
        StatusLevel.Warning => Tone.Warning,
        StatusLevel.Bad => Tone.Danger,
        StatusLevel.Busy => Tone.Info,
        QuoteRequestStatus.Running => Tone.Info,
        QuoteRequestStatus.Completed => Tone.Success,
        QuoteRequestStatus.CompletedWithErrors => Tone.Warning,
        QuoteRequestStatus.Failed => Tone.Danger,
        QuoteResultStatus.Ok => Tone.Success,
        QuoteResultStatus.Error => Tone.Danger,
        QuoteResultStatus.Invalid => Tone.Warning,
        _ => Tone.Neutral,
    };
}

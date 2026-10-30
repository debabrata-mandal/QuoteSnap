using System.Windows;
using System.Windows.Controls;

namespace QuoteSnap.Desktop.Controls;

/// <summary>A rounded, tinted status badge with a dot; Info pulses to show work in progress. Styled in Themes/Controls.xaml.</summary>
public sealed class StatusPill : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(null));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(Tone), typeof(StatusPill), new PropertyMetadata(Tone.Neutral));

    static StatusPill() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusPill), new FrameworkPropertyMetadata(typeof(StatusPill)));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Tone Tone
    {
        get => (Tone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}

/// <summary>An indeterminate ring spinner. Styled in Themes/Controls.xaml.</summary>
public sealed class Spinner : Control
{
    static Spinner() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(typeof(Spinner)));
}

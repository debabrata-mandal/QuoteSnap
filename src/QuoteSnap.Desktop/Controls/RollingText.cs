using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuoteSnap.Desktop.Controls;

/// <summary>
/// Text whose changed characters roll like an odometer: up when the number in it rose, down when it fell, briefly tinted green or red.
/// With <see cref="Key"/> the last text is remembered by key, so a tile rebuilt for each tick still rolls from its previous value
/// and a recycled grid row doesn't roll from another row's value. Prefix keys with the page so pages don't share them.
/// </summary>
public sealed class RollingText : ContentControl
{
    private static readonly Dictionary<string, string> LastTexts = new(StringComparer.Ordinal);
    private static readonly TimeSpan RollTime = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan TintTime = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(35);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(RollingText), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.Register(nameof(Key), typeof(string), typeof(RollingText), new PropertyMetadata(null, OnChanged));

    private readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal };
    private string? _shown;
    private bool _pending;

    public RollingText()
    {
        Focusable = false;
        IsTabStop = false;
        Content = _cells;
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    // Key and Text bind in no set order (a recycled row gets both anew), so a shown control compares once data binding is done.
    // A new one (a dashboard tile rebuilt for a tick) updates as soon as both are set, otherwise it would render blank for a frame.
    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        RollingText rolling = (RollingText)d;
        if (rolling._shown is null && rolling.Text is not null && rolling.Key is not null)
        {
            rolling.Update();
            return;
        }

        if (rolling._pending)
            return;

        rolling._pending = true;
        rolling.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () =>
        {
            rolling._pending = false;
            rolling.Update();
        });
    }

    private void Update()
    {
        if (Text is not { } text)
            return;

        string? previous = _shown;
        // A row dropped from the screen (a sorted list rebuilds its rows on every refresh) must not claim the new text as seen, or its replacement wouldn't roll.
        if (_shown is not null && PresentationSource.FromVisual(this) is null)
        {
            Show(text);
            _shown = text;
            return;
        }

        if (Key is { } key)
        {
            previous = LastTexts.GetValueOrDefault(key);
            LastTexts[key] = text;
        }

        if (previous is null || previous == text)
        {
            if (_shown != text)
                Show(text);
        }
        else if (PresentationSource.FromVisual(this) is null)
        {
            // Not on screen yet (a rebuilt row): its font isn't inherited yet, so roll once loaded, when the digit height is known.
            Show(previous);
            RoutedEventHandler? rollWhenLoaded = null;
            rollWhenLoaded = (_, _) =>
            {
                Loaded -= rollWhenLoaded;
                if (_shown == text)
                    Roll(previous, text);
            };
            Loaded += rollWhenLoaded;
        }
        else
        {
            Roll(previous, text);
        }
        _shown = text;
    }

    private void Show(string text)
    {
        _cells.Children.Clear();
        _cells.Children.Add(new TextBlock { Text = text });
    }

    private void Roll(string from, string to)
    {
        _cells.Children.Clear();
        int up = Compare(from, to);
        double height = FontSize * FontFamily.LineSpacing;
        Color tint = Application.Current.TryFindResource(up >= 0 ? "SuccessColor" : "DangerColor") is Color c ? c : Colors.Gray;
        // Pairing characters only makes sense when both texts have the same shape; otherwise a digit would roll into a comma and leave a gap.
        if (Shape(from) != Shape(to))
        {
            _cells.Children.Add(Cell(from, to, up, height, TimeSpan.Zero, tint));
            return;
        }

        int rolled = 0;
        for (int i = to.Length - 1; i >= 0; i--)
        {
            char next = to[i];
            char prior = from[i];
            if (next == prior)
            {
                _cells.Children.Insert(0, new TextBlock { Text = next.ToString() });
                continue;
            }

            _cells.Children.Insert(0, Cell(prior.ToString(), next.ToString(), up, height, Stagger * rolled++, tint));
        }
    }

    // The outgoing text leaves the cell once it has rolled out, so the cell settles to the incoming text's width instead of keeping a wider old one.
    private Grid Cell(string prior, string next, int up, double height, TimeSpan delay, Color tint)
    {
        double direction = up >= 0 ? 1 : -1;
        Grid cell = new() { ClipToBounds = true };
        TextBlock outgoing = new() { Text = prior, HorizontalAlignment = HorizontalAlignment.Right };
        cell.Children.Add(Slide(outgoing, 0, -direction * height, delay, () => cell.Children.Remove(outgoing)));
        TextBlock incoming = Slide(new TextBlock { Text = next, HorizontalAlignment = HorizontalAlignment.Right }, direction * height, 0, delay);
        Tint(incoming, tint);
        cell.Children.Add(incoming);
        return cell;
    }

    private static string Shape(string text) => new([.. text.Select(ch => char.IsDigit(ch) ? '0' : ch)]);

    private static TextBlock Slide(TextBlock block, double from, double to, TimeSpan delay, Action? done = null)
    {
        TranslateTransform move = new(0, from);
        block.RenderTransform = move;
        DoubleAnimation animation = new(from, to, RollTime) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (done is not null)
            animation.Completed += (_, _) => done();
        move.BeginAnimation(TranslateTransform.YProperty, animation);
        return block;
    }

    private void Tint(TextBlock block, Color tint)
    {
        if (Foreground is not SolidColorBrush { Color: Color normal })
            return;

        SolidColorBrush brush = new(tint);
        block.Foreground = brush;
        ColorAnimation fade = new(tint, normal, TintTime) { BeginTime = RollTime, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => block.ClearValue(TextBlock.ForegroundProperty);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }

    /// <summary>1 when the number in the text rose, -1 when it fell; "▼" or a minus sign makes it negative.</summary>
    private static int Compare(string from, string to) =>
        Number(from) is { } a && Number(to) is { } b ? b.CompareTo(a) : 1;

    private static decimal? Number(string text)
    {
        string digits = new([.. text.Where(ch => char.IsDigit(ch) || ch == '.')]);
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            return null;
        return text.Contains('▼') || text.Contains('-') || text.Contains('−') ? -value : value;
    }
}

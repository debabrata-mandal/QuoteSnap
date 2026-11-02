using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Controls;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop;

/// <summary>Status values (StatusLevel, request and quote statuses) to their <see cref="Tone"/>.</summary>
public sealed class ToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Tones.Of(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Status values to the tone's brush from Themes/Tokens.xaml; pass "subtle" for the tinted background or "border" for the outline.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string variant = parameter switch
        {
            "subtle" => "Subtle",
            "border" => "Border",
            _ => "",
        };
        string key = $"{Tones.Of(value)}{variant}Brush";
        return Application.Current.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => StatusText.Of(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Green for a rise, red for a fall; leaves the default colour for zero or no value.</summary>
public sealed class SignToBrushConverter : IValueConverter
{
    /// <param name="parameter">"subtle" for the tinted background brushes, with a neutral fill for no change.</param>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string suffix = parameter is "subtle" ? "SubtleBrush" : "Brush";
        string? key = value switch
        {
            decimal d when d > 0 => "Success" + suffix,
            decimal d when d < 0 => "Danger" + suffix,
            _ => parameter is "subtle" ? "FillBrush" : null,
        };
        return key is null ? DependencyProperty.UnsetValue : Application.Current.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"▲ 32.80" or "▼ 7.95"; with the "percent" parameter "+1.32%" or "−0.82%".</summary>
public sealed class ChangeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not decimal change)
            return Display.Missing;

        string magnitude = Math.Abs(change).ToString("N2", CultureInfo.CurrentCulture);
        if (parameter is "percent")
            return change > 0 ? $"+{magnitude}%" : change < 0 ? $"−{magnitude}%" : $"{magnitude}%";
        return change > 0 ? $"▲ {magnitude}" : change < 0 ? $"▼ {magnitude}" : magnitude;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Local time; the parameter picks the format: "when" (Today, 11:37:54), "clock" (11:37:54) or "short" (30 Oct 11:37).</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value, parameter) switch
    {
        (DateTimeOffset time, "when") => Display.When(time),
        (DateTimeOffset time, "clock") => time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
        (DateTimeOffset time, "short") => time.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture),
        (DateTimeOffset time, _) => Display.Time(time),
        (DateOnly date, _) => Display.Date(date),
        _ => Display.Missing,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is null; pass "invert" as the parameter for visible when it isn't.</summary>
public sealed class VisibleWhenNullConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is null || value is string { Length: 0 }) != (parameter is "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the collection is empty (or null); pass "invert" for visible when it has items.</summary>
public sealed class VisibleWhenEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool empty = value switch
        {
            ICollection collection => collection.Count == 0,
            IEnumerable sequence => !sequence.GetEnumerator().MoveNext(),
            _ => true,
        };
        return empty != (parameter is "invert") ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True when the value's text equals the parameter. Converting back from true yields the parameter,
/// which lets radio buttons drive an index or a filter name.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string text)
            return Binding.DoNothing;
        return targetType == typeof(int) ? int.Parse(text, CultureInfo.InvariantCulture) : text;
    }
}

public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpper(culture) ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A quote's "low – high", or a dash when it has no prices.</summary>
public sealed class DayRangeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is QuoteResultDto { Low: { } low, High: { } high }
        ? $"{low.ToString("N2", CultureInfo.CurrentCulture)} – {high.ToString("N2", CultureInfo.CurrentCulture)}"
        : Display.Missing;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ShortLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string text
        ? Display.ShortLabel(text)
        : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
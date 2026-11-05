using System.Windows;
using System.Windows.Media;
using QuoteSnap.Desktop.Settings;

namespace QuoteSnap.Desktop.Services;

/// <summary>
/// Swaps the colour palette (the first merged dictionary in App.xaml) at runtime. XAML picks the change up through
/// DynamicResource; brushes handed out by converters come from <see cref="Brush"/> and are recoloured in place.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, SolidColorBrush> LiveBrushes = [];

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static bool IsDark => Application.Current.TryFindResource("IsDarkTheme") is not false;

    public static event EventHandler? Changed;

    public static void Apply(AppTheme theme)
    {
        ResourceDictionary palette = new() { Source = new Uri($"/QuoteSnap;component/Themes/Palettes/{theme}.xaml", UriKind.Relative) };
        Application.Current.Resources.MergedDictionaries[0] = palette;
        foreach ((string key, SolidColorBrush brush) in LiveBrushes)
        {
            if (palette[key] is SolidColorBrush source)
                brush.Color = source.Color;
        }

        Current = theme;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The palette brush for a key, for code that can't use DynamicResource; solid brushes follow later theme changes.</summary>
    public static object Brush(string key)
    {
        if (LiveBrushes.TryGetValue(key, out SolidColorBrush? live))
            return live;

        switch (Application.Current.TryFindResource(key))
        {
            case SolidColorBrush solid:
                live = new SolidColorBrush(solid.Color);
                LiveBrushes[key] = live;
                return live;
            case object other:
                return other;
            default:
                return DependencyProperty.UnsetValue;
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace QuoteSnap.Desktop.Controls;

/// <summary>Attached properties the control templates in Themes/Controls.xaml read.</summary>
public static class Ui
{
    private const long MaxDroppedFileBytes = 5 * 1024 * 1024;

    /// <summary>A Segoe Fluent Icons glyph shown before a button's or nav item's content.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new PropertyMetadata(null));

    /// <summary>Text shown in an empty TextBox.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(8)));

    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.RegisterAttached("HoverBrush", typeof(Brush), typeof(Ui), new PropertyMetadata(null));

    /// <summary>Spins the button's icon, e.g. a refresh icon while refreshing.</summary>
    public static readonly DependencyProperty IsSpinningProperty =
        DependencyProperty.RegisterAttached("IsSpinning", typeof(bool), typeof(Ui), new PropertyMetadata(false));

    /// <summary>Shows the hover state without the mouse being over the element in WPF's eyes (the maximize button under snap layouts).</summary>
    public static readonly DependencyProperty IsHotProperty =
        DependencyProperty.RegisterAttached("IsHot", typeof(bool), typeof(Ui), new PropertyMetadata(false));

    /// <summary>Runs the command with the text of a .txt or .csv file dropped onto the element.</summary>
    public static readonly DependencyProperty FileDropCommandProperty =
        DependencyProperty.RegisterAttached("FileDropCommand", typeof(ICommand), typeof(Ui), new PropertyMetadata(null, OnFileDropCommandChanged));

    /// <summary>Passes mouse-wheel scrolling to the parent, for a short grid inside a scrolling page.</summary>
    public static readonly DependencyProperty BubbleMouseWheelProperty =
        DependencyProperty.RegisterAttached("BubbleMouseWheel", typeof(bool), typeof(Ui), new PropertyMetadata(false, OnBubbleMouseWheelChanged));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static Brush? GetHoverBrush(DependencyObject element) => (Brush?)element.GetValue(HoverBrushProperty);

    public static void SetHoverBrush(DependencyObject element, Brush? value) => element.SetValue(HoverBrushProperty, value);

    public static bool GetIsSpinning(DependencyObject element) => (bool)element.GetValue(IsSpinningProperty);

    public static void SetIsSpinning(DependencyObject element, bool value) => element.SetValue(IsSpinningProperty, value);

    public static bool GetIsHot(DependencyObject element) => (bool)element.GetValue(IsHotProperty);

    public static void SetIsHot(DependencyObject element, bool value) => element.SetValue(IsHotProperty, value);

    public static ICommand? GetFileDropCommand(DependencyObject element) => (ICommand?)element.GetValue(FileDropCommandProperty);

    public static void SetFileDropCommand(DependencyObject element, ICommand? value) => element.SetValue(FileDropCommandProperty, value);

    public static bool GetBubbleMouseWheel(DependencyObject element) => (bool)element.GetValue(BubbleMouseWheelProperty);

    public static void SetBubbleMouseWheel(DependencyObject element, bool value) => element.SetValue(BubbleMouseWheelProperty, value);

    private static void OnBubbleMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if (e.NewValue is true)
            element.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement { Parent: UIElement parent })
            return;

        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }

    private static void OnFileDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.AllowDrop = e.NewValue is not null;
        element.PreviewDragOver -= OnPreviewDragOver;
        element.PreviewDrop -= OnPreviewDrop;
        if (e.NewValue is not null)
        {
            element.PreviewDragOver += OnPreviewDragOver;
            element.PreviewDrop += OnPreviewDrop;
        }
    }

    // TextBox handles drag events itself, so files are only caught in the preview (tunnelling) phase.
    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = e.Data.GetDataPresent(DataFormats.FileDrop);
    }

    private static void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files || sender is not DependencyObject target)
            return;

        e.Handled = true;
        FileInfo file = new(files[0]);
        if (!file.Exists || file.Length > MaxDroppedFileBytes)
            return;

        ICommand? command = GetFileDropCommand(target);
        string text = File.ReadAllText(file.FullName);
        if (command?.CanExecute(text) == true)
            command.Execute(text);
    }
}

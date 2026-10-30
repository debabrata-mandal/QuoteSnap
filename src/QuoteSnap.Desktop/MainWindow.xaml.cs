using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using QuoteSnap.Desktop.Controls;
using QuoteSnap.Desktop.Interop;

namespace QuoteSnap.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => OnWindowStateChanged();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
        if (WindowEffects.Apply(source.Handle))
        {
            // Mica shows through wherever WPF draws nothing, so the surfaces switch to their translucent versions.
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            Background = Brushes.Transparent;
            ContentBackground.Background = (Brush)FindResource("BackgroundMicaBrush");
            Sidebar.Background = (Brush)FindResource("SidebarMicaBrush");
        }

        source.AddHook(WndProc);
    }

    private void OnWindowStateChanged()
    {
        // A maximized WindowChrome window extends past the screen by the resize border.
        Thickness border = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = WindowState == WindowState.Maximized
            ? new Thickness(border.Left + 4, border.Top + 4, border.Right + 4, border.Bottom + 4)
            : new Thickness(0);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    // Reporting the maximize button as HTMAXBUTTON lets Windows 11 show the snap layouts flyout over it.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WindowEffects.WM_NCHITTEST:
                bool overMaximize = IsOverMaximize(lParam);
                Ui.SetIsHot(MaximizeButton, overMaximize);
                if (overMaximize)
                {
                    handled = true;
                    return WindowEffects.HTMAXBUTTON;
                }

                break;
            case WindowEffects.WM_NCMOUSELEAVE:
                Ui.SetIsHot(MaximizeButton, false);
                break;
            case WindowEffects.WM_NCLBUTTONDOWN when wParam.ToInt32() == WindowEffects.HTMAXBUTTON:
                handled = true;
                break;
            case WindowEffects.WM_NCLBUTTONUP when wParam.ToInt32() == WindowEffects.HTMAXBUTTON:
                handled = true;
                ToggleMaximize();
                break;
        }

        return IntPtr.Zero;
    }

    private bool IsOverMaximize(IntPtr lParam)
    {
        if (!MaximizeButton.IsVisible)
            return false;

        (int x, int y) = WindowEffects.PointFromLParam(lParam);
        Point point = MaximizeButton.PointFromScreen(new Point(x, y));
        return point.X >= 0 && point.Y >= 0 && point.X < MaximizeButton.ActualWidth && point.Y < MaximizeButton.ActualHeight;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

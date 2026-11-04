using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuoteSnap.Desktop.Controls;
using QuoteSnap.Desktop.Interop;

namespace QuoteSnap.Desktop;

public partial class MainWindow : Window
{
    /// <summary>Until the user moves, resizes or maximizes the window, it grows to fit the dashboard.</summary>
    private bool _fitDashboard = true;

    /// <summary>Set while the window sizes itself, to tell its own changes from the user's (including snapping with the keyboard).</summary>
    private bool _fitting;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => OnWindowStateChanged();
        Loaded += (_, _) => FitToMonitor(Height);
        SizeChanged += (_, _) => StopFittingUnlessOwnChange();
        LocationChanged += (_, _) => StopFittingUnlessOwnChange();
        DashboardPage.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnDashboardScrollChanged));
    }

    /// <summary>Grows the window as the dashboard's cards arrive, so it shows them all without scrolling where the monitor is tall enough.</summary>
    private void OnDashboardScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_fitDashboard && WindowState == WindowState.Normal && e.OriginalSource == DashboardPage.Content && e.ExtentHeight > e.ViewportHeight)
            FitToMonitor(Height + Math.Ceiling(e.ExtentHeight - e.ViewportHeight));
    }

    /// <summary>Sets the height, capped with the width to the work area of the window's monitor, and centres the window on it.</summary>
    private void FitToMonitor(double height)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
            return;

        (int left, int top, int right, int bottom) = WindowEffects.MonitorWorkArea(source.Handle);
        Matrix toDips = source.CompositionTarget.TransformFromDevice;
        Point topLeft = toDips.Transform(new Point(left, top));
        Point bottomRight = toDips.Transform(new Point(right, bottom));
        _fitting = true;
        try
        {
            Width = Math.Min(Width, bottomRight.X - topLeft.X);
            Height = Math.Min(height, bottomRight.Y - topLeft.Y);
            Left = topLeft.X + (bottomRight.X - topLeft.X - Width) / 2;
            Top = topLeft.Y + (bottomRight.Y - topLeft.Y - Height) / 2;
        }
        finally
        {
            _fitting = false;
        }
    }

    private void StopFittingUnlessOwnChange()
    {
        if (IsLoaded && !_fitting)
            _fitDashboard = false;
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
        _fitDashboard = false;
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
            case WindowEffects.WM_ENTERSIZEMOVE:
                _fitDashboard = false;
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

using System.Runtime.InteropServices;

namespace QuoteSnap.Desktop.Interop;

/// <summary>The few Win32/DWM calls the custom window chrome needs.</summary>
internal static partial class WindowEffects
{
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCMOUSELEAVE = 0x02A2;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_ENTERSIZEMOVE = 0x0231;
    public const int HTMAXBUTTON = 9;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2;

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x00080000;

    private const int MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>Windows 11 22H2, the first build with DWMWA_SYSTEMBACKDROP_TYPE.</summary>
    private const int MicaBuild = 22621;

    /// <summary>Rounded corners; returns whether the Mica backdrop was applied.</summary>
    public static bool Apply(IntPtr hwnd)
    {
        SetInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        if (Environment.OSVersion.Version.Build < MicaBuild || !SetInt(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW))
            return false;

        // DWM draws its own caption buttons for a WS_SYSMENU window, and they show through the translucent Mica surfaces.
        SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
        return true;
    }

    /// <summary>Dark or light frame and Mica tint, to match the app's theme.</summary>
    public static void SetDarkMode(IntPtr hwnd, bool dark) => SetInt(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);

    /// <summary>The screen point packed into a WM_NCHITTEST lParam.</summary>
    public static (int X, int Y) PointFromLParam(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        return ((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    /// <summary>The work area (the monitor minus the taskbar), in device pixels, of the monitor the window is on.</summary>
    public static (int Left, int Top, int Right, int Bottom) MonitorWorkArea(IntPtr hwnd)
    {
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info);
        return (info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    private static bool SetInt(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }
}

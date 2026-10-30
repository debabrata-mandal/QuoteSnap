using System.Runtime.InteropServices;

namespace QuoteSnap.Desktop.Interop;

/// <summary>The few Win32/DWM calls the custom window chrome needs.</summary>
internal static partial class WindowEffects
{
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCMOUSELEAVE = 0x02A2;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int HTMAXBUTTON = 9;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2;

    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x00080000;

    /// <summary>Windows 11 22H2, the first build with DWMWA_SYSTEMBACKDROP_TYPE.</summary>
    private const int MicaBuild = 22621;

    /// <summary>Dark caption and rounded corners; returns whether the Mica backdrop was applied.</summary>
    public static bool Apply(IntPtr hwnd)
    {
        SetInt(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        SetInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        if (Environment.OSVersion.Version.Build < MicaBuild || !SetInt(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW))
            return false;

        // DWM draws its own caption buttons for a WS_SYSMENU window, and they show through the translucent Mica surfaces.
        SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
        return true;
    }

    /// <summary>The screen point packed into a WM_NCHITTEST lParam.</summary>
    public static (int X, int Y) PointFromLParam(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        return ((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }

    private static bool SetInt(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);
}

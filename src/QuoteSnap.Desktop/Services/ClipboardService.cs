using System.Runtime.InteropServices;
using System.Windows;

namespace QuoteSnap.Desktop.Services;

public static class ClipboardService
{
    /// <returns>False when another app holds the clipboard open.</returns>
    public static bool TryCopy(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}

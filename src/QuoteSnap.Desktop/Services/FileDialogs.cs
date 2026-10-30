using System.IO;
using System.Text;
using Microsoft.Win32;

namespace QuoteSnap.Desktop.Services;

public sealed class FileDialogs
{
    public string? OpenText(string title)
    {
        OpenFileDialog dialog = new() { Title = title, Filter = "Text and CSV files|*.txt;*.csv|All files|*.*" };
        return dialog.ShowDialog() == true ? File.ReadAllText(dialog.FileName) : null;
    }

    /// <returns>The saved file's path, or null if the user cancelled.</returns>
    public string? SaveCsv(string suggestedName, string contents)
    {
        SaveFileDialog dialog = new() { FileName = suggestedName, DefaultExt = ".csv", Filter = "CSV files|*.csv" };
        if (dialog.ShowDialog() != true)
            return null;

        // The BOM makes Excel read the file as UTF-8.
        File.WriteAllText(dialog.FileName, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return dialog.FileName;
    }
}

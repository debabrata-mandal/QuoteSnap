namespace QuoteSnap.Desktop.Text;

/// <summary>Splits pasted or loaded text into entries: one per line, or separated by commas, semicolons, tabs or spaces.</summary>
public static class ListInput
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>Keeps the first occurrence of each entry (ignoring case) in the original order.</summary>
    public static List<string> Parse(string? text, bool upperCase = false)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> entries = [];
        foreach (string part in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string entry = part.Trim('"', '\'');
            if (entry.Length > 0 && seen.Add(entry))
                entries.Add(upperCase ? entry.ToUpperInvariant() : entry);
        }

        return entries;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace QuoteSnap.Api.Nav;

/// <summary>
/// Reads AMFI's scheme master CSV for its "Scheme NAV Name" column ("Motilal Oswal Midcap Fund-Direct Plan-Growth Option").
/// NAVAll.txt leaves Plan and Option blank for some schemes; this name is the only place that says which plan they are.
/// </summary>
public static partial class AmfiSchemeMaster
{
    /// <returns>NAV names by scheme code.</returns>
    /// <exception cref="FormatException">The file has no "Code" or "Scheme NAV Name" column.</exception>
    public static IReadOnlyDictionary<int, string> ParseNavNames(string text)
    {
        string[] lines = text.Split('\n');
        int headerIndex = Array.FindIndex(lines, l => l.Contains("Scheme NAV Name", StringComparison.OrdinalIgnoreCase));
        if (headerIndex < 0)
            throw new FormatException("The scheme master file has no 'Scheme NAV Name' column.");

        string[] header = [.. lines[headerIndex].Split(',').Select(h => h.Trim())];
        int codeColumn = Array.FindIndex(header, h => h.Equals("Code", StringComparison.OrdinalIgnoreCase));
        int navNameColumn = Array.FindIndex(header, h => h.Equals("Scheme NAV Name", StringComparison.OrdinalIgnoreCase));
        if (codeColumn < 0)
            throw new FormatException("The scheme master file has no 'Code' column.");

        Dictionary<int, string> navNames = [];
        foreach (string line in lines.Skip(headerIndex + 1))
        {
            // No field is quoted, so a row with a comma inside a field can't be split reliably.
            string[] fields = line.TrimEnd('\r').Split(',');
            if (fields.Length != header.Length)
                continue;
            if (int.TryParse(fields[codeColumn].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int code)
                && fields[navNameColumn].Trim() is { Length: > 0 } navName)
            {
                navNames[code] = navName;
            }
        }

        return navNames;
    }

    /// <summary>"…-Direct Plan-Growth", "… - Direct Growth" or "…-Direct-IDCW"; not "Defunct Plan".</summary>
    public static bool IsDirect(string navName) => DirectWord().IsMatch(navName);

    /// <summary>
    /// The option part of a NAV name: "Growth Option" from "Motilal Oswal Midcap Fund-Direct Plan-Growth Option",
    /// "Quarterly IDCW Option" from "HDFC FMP 1204D December - Quarterly IDCW Option - Direct Plan".
    /// </summary>
    /// <returns>Null when the name has no option, as in "Motilal Oswal BSE Low Volatility Index Fund-Direct plan".</returns>
    public static string? OptionFrom(string navName, string schemeName)
    {
        Match direct = DirectWord().Match(navName);
        if (!direct.Success)
            return null;

        string after = navName[(direct.Index + direct.Length)..];
        string before = direct.Index >= schemeName.Length && navName.StartsWith(schemeName, StringComparison.OrdinalIgnoreCase)
            ? navName[schemeName.Length..direct.Index]
            : "";
        string option = string.Join(" ", new[] { before, after }
            .Select(part => SpaceRun().Replace(part.Trim(' ', '-', '–'), " "))
            .Where(part => part.Length > 0));
        return option.Length > 0 ? option : null;
    }

    [GeneratedRegex(@"\bDirect(\s+Plan)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DirectWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRun();
}

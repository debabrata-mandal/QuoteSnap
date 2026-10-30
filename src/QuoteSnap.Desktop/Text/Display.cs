using System.Globalization;

namespace QuoteSnap.Desktop.Text;

public static class Display
{
    public const string Missing = "—";

    public static string Time(DateTimeOffset? value) =>
        value is { } time ? time.ToLocalTime().ToString("dd MMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture) : Missing;

    /// <summary>"Today, 11:37:54", "Yesterday, 18:02:10" or "28 Oct, 14:02:10".</summary>
    public static string When(DateTimeOffset? value, DateTime? today = null)
    {
        if (value is not { } time)
            return Missing;

        DateTime local = time.ToLocalTime().DateTime;
        DateTime day = (today ?? DateTime.Today).Date;
        string clock = local.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        return local.Date == day ? $"Today, {clock}"
            : local.Date == day.AddDays(-1) ? $"Yesterday, {clock}"
            : $"{local.ToString("dd MMM", CultureInfo.CurrentCulture)}, {clock}";
    }

    /// <summary><see cref="When"/> for the middle of a sentence: "today, 11:37:54" or "on 28 Oct, 14:02:10".</summary>
    public static string WhenInSentence(DateTimeOffset? value, DateTime? today = null)
    {
        string text = When(value, today);
        return text == Missing ? text
            : text.StartsWith("Today", StringComparison.Ordinal) || text.StartsWith("Yesterday", StringComparison.Ordinal)
                ? char.ToLowerInvariant(text[0]) + text[1..]
                : "on " + text;
    }

    /// <summary>"1 symbol", "2 symbols"; pass the plural when it isn't singular + "s".</summary>
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count.ToString("N0", CultureInfo.CurrentCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";

    /// <summary>The last 8 hex digits of a version 7 GUID; the leading digits are a timestamp shared by requests made close together.</summary>
    public static string ShortId(Guid id) => id.ToString("N")[^8..];

    /// <summary>AMFI's "Direct Plan" and "GROWTH Option" become "Direct" and "Growth" for chips.</summary>
    public static string ShortLabel(string label)
    {
        string text = label.Replace(" Plan", "", StringComparison.OrdinalIgnoreCase).Replace(" Option", "", StringComparison.OrdinalIgnoreCase).Trim();
        return text.Any(char.IsLower) ? text : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
    }

    public static string Date(DateOnly? value) =>
        value is { } date ? date.ToString("dd MMM yyyy", CultureInfo.CurrentCulture) : Missing;

    public static string Duration(TimeSpan? value) => value switch
    {
        null => Missing,
        { TotalSeconds: < 60 } span => $"{span.TotalSeconds:0.0} s",
        { TotalHours: < 1 } span => $"{(int)span.TotalMinutes} min {span.Seconds} s",
        { TotalDays: < 1 } span => $"{(int)span.TotalHours} h {span.Minutes} min",
        { TotalDays: < 2 } span => $"1 day {span.Hours} h",
        { } span => $"{(int)span.TotalDays} days {span.Hours} h",
    };

    /// <summary>"1.0.0+7dc8006187cab0d1…" becomes "1.0.0 (7dc8006)".</summary>
    public static string Version(string version) => version.Split('+', 2) switch
    {
        [string number, string commit] => $"{number} ({commit[..Math.Min(7, commit.Length)]})",
        _ => version,
    };
}

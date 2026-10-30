namespace QuoteSnap.Desktop.Settings;

public sealed record DesktopSettings(Uri BaseAddress, string? ApiKey)
{
    public static readonly Uri DefaultBaseAddress = new("http://127.0.0.1:8080/");

    /// <summary>Accepts an absolute http(s) URL and adds the trailing slash relative API paths need.</summary>
    public static bool TryParseBaseAddress(string? text, out Uri address)
    {
        address = DefaultBaseAddress;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme is not ("http" or "https"))
        {
            return false;
        }

        address = parsed.AbsoluteUri.EndsWith('/') ? parsed : new Uri(parsed.AbsoluteUri + "/");
        return true;
    }
}

/// <param name="Source">Where the settings came from, shown on the Settings tab.</param>
public sealed record LoadedSettings(DesktopSettings Settings, string Source, bool IsSaved);

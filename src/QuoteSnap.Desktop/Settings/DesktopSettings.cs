namespace QuoteSnap.Desktop.Settings;

/// <param name="AdminApiKey">Only needed for the Fyers login; must match the API's Auth__AdminApiKey.</param>
public sealed record DesktopSettings(Uri BaseAddress, string? ApiKey, string? AdminApiKey = null)
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

/// <summary>The Fyers API app (from myapi.fyers.in) and account PIN that a login hands to the service.</summary>
public sealed record FyersLoginDetails(string? AppId, string? SecretId, string? Pin, string RedirectUri);

/// <param name="IsRemembered">Whether the details were saved on this PC rather than read from .env.</param>
/// <param name="Source">Where the details came from, shown on the Fyers login page.</param>
public sealed record LoadedFyersLogin(FyersLoginDetails Details, bool IsRemembered, string Source);

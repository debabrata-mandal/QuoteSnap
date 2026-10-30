namespace QuoteSnap.Api.Fyers;

public class FyersApiException(string message, int? httpStatus = null, int? fyersCode = null)
    : Exception(message)
{
    public int? HttpStatus { get; } = httpStatus;

    public int? FyersCode { get; } = fyersCode;

    // Fyers token error codes; verify against the current Fyers v3 error reference.
    private static readonly HashSet<int> AuthErrorCodes = [-8, -15, -16, -17];

    public static FyersApiException Create(int httpStatus, int? fyersCode, string? message)
    {
        string text = message is { Length: > 0 } ? message : $"Fyers request failed with HTTP {httpStatus}";
        if (fyersCode is not null)
            text = $"{text} (code {fyersCode})";

        if (httpStatus is 401 or 403 || fyersCode is { } code && AuthErrorCodes.Contains(code))
            return new FyersAuthException(text, httpStatus, fyersCode);

        if (httpStatus == 429 || fyersCode == 429)
            return new FyersRateLimitException(text, httpStatus, fyersCode);

        return new FyersApiException(text, httpStatus, fyersCode);
    }
}

/// <summary>The access token was rejected or has expired.</summary>
public sealed class FyersAuthException(string message, int? httpStatus = null, int? fyersCode = null)
    : FyersApiException(message, httpStatus, fyersCode);

public sealed class FyersRateLimitException(string message, int? httpStatus = null, int? fyersCode = null)
    : FyersApiException(message, httpStatus, fyersCode);

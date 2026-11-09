namespace QuoteSnap.Api.Zerodha;

public class KiteApiException(string message, int? httpStatus = null, string? errorType = null) : Exception(message)
{
    public int? HttpStatus { get; } = httpStatus;

    /// <summary>Kite's error_type, such as "TokenException" or "InputException".</summary>
    public string? ErrorType { get; } = errorType;

    public static KiteApiException Create(int httpStatus, string? errorType, string? message)
    {
        string text = message is { Length: > 0 } ? message : $"Kite request failed with HTTP {httpStatus}";
        return httpStatus == 403 || errorType == "TokenException" ? new KiteTokenException(text, httpStatus, errorType) : new KiteApiException(text, httpStatus, errorType);
    }
}

/// <summary>The access token or request token was rejected or has expired.</summary>
public sealed class KiteTokenException(string message, int? httpStatus = null, string? errorType = null) : KiteApiException(message, httpStatus, errorType);

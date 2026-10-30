using System.Text.Json.Serialization;

namespace QuoteSnap.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<QuoteRequestStatus>))]
public enum QuoteRequestStatus
{
    Queued,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<QuoteResultStatus>))]
public enum QuoteResultStatus
{
    Pending,
    Ok,
    Error,
    Invalid,
}

[JsonConverter(typeof(JsonStringEnumConverter<FyersAuthState>))]
public enum FyersAuthState
{
    Ok,
    AccessTokenExpired,
    ReloginRequired,
}

public static class QuoteRequestStatusExtensions
{
    public static bool IsTerminal(this QuoteRequestStatus status) =>
        status is QuoteRequestStatus.Completed or QuoteRequestStatus.CompletedWithErrors or QuoteRequestStatus.Failed;
}

using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Text;

public static class StatusText
{
    public static string Of(object? value) => value switch
    {
        QuoteRequestStatus.CompletedWithErrors => "Completed with errors",
        QuoteResultStatus.Ok => "OK",
        null => "",
        _ => value.ToString() ?? "",
    };
}

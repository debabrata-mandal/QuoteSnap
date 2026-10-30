using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class NewRequestViewModel : ObservableObject
{
    private const int InvalidSymbolsShown = 12;

    private readonly QuoteSnapConnection _connection;
    private readonly FileDialogs _dialogs;

    public NewRequestViewModel(QuoteSnapConnection connection, FileDialogs dialogs)
    {
        _connection = connection;
        _dialogs = dialogs;
        Results = new ResultSetViewModel(dialogs, "Quotes appear here as each batch of 50 completes.");
    }

    public event EventHandler? RequestFinished;

    public ResultSetViewModel Results { get; }

    [ObservableProperty]
    public partial string SymbolsText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyPropertyChangedFor(nameof(SymbolSummary))]
    public partial int SymbolCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SymbolSummary))]
    public partial int LineCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRequest), nameof(Status), nameof(StatusLabel), nameof(StatusTone), nameof(RequestId), nameof(ShortRequestId), nameof(Total))]
    public partial QuoteRequestDetails? Details { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRequest))]
    public partial bool IsSubmitting { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    public partial string? ElapsedText { get; set; }

    [ObservableProperty]
    public partial string? InvalidSymbolsText { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public string SymbolSummary => SymbolCount == LineCount || SymbolCount == 0
        ? Display.Count(SymbolCount, "symbol")
        : $"{Display.Count(SymbolCount, "unique symbol")} · {Display.Count(LineCount, "line")}";

    public bool HasRequest => Details is not null || IsSubmitting;

    public QuoteRequestStatus? Status => Details?.Status;

    public string StatusLabel => Details is { } details ? StatusText.Of(details.Status) : "Submitting";

    /// <summary>The request status, or Busy while it's being submitted; for the status pill's tone.</summary>
    public object StatusTone => (object?)Details?.Status ?? StatusLevel.Busy;

    public string? RequestId => Details?.RequestId.ToString();

    public string? ShortRequestId => Details is { } details ? Display.ShortId(details.RequestId) : null;

    public int Total => Details?.TotalSymbols ?? 0;

    public string PercentText => $"{Math.Floor(ProgressPercent):0}%";

    partial void OnSymbolsTextChanged(string value)
    {
        SymbolCount = ListInput.Parse(value).Count;
        LineCount = value.Split('\n').Count(line => line.Trim().Length > 0);
    }

    private bool CanSubmit() => SymbolCount > 0;

    [RelayCommand(CanExecute = nameof(CanSubmit), IncludeCancelCommand = true)]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        List<string> symbols = ListInput.Parse(SymbolsText);
        QuoteSnapClient client = _connection.Client;
        Details = null;
        Results.Results = [];
        Results.Message = null;
        ProgressPercent = 0;
        IsIndeterminate = true;
        IsSubmitting = true;
        ProgressText = $"Submitting {Display.Count(symbols.Count, "symbol")}…";
        ElapsedText = null;
        InvalidSymbolsText = null;
        Message = null;
        try
        {
            QuoteRequestAccepted accepted = await client.SubmitAsync(symbols, cancellationToken);
            InvalidSymbolsText = DescribeInvalid(accepted.InvalidSymbols);
            ProgressText = "Queued…";
            Progress<QuoteRequestDetails> progress = new(Show);
            Show(await client.WaitForCompletionAsync(accepted.RequestId, progress, cancellationToken));
            RequestFinished?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report(StatusLevel.Unknown, "Stopped watching. The request keeps running on the server; you'll find it on the History page.");
        }
        catch (Exception ex)
        {
            ProgressText = null;
            Report(StatusLevel.Bad, ErrorText.Describe(ex, _connection.Settings.BaseAddress));
        }
        finally
        {
            IsIndeterminate = false;
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private void LoadFile()
    {
        if (_dialogs.OpenText("Load symbols") is { } text)
            SymbolsText = text;
    }

    [RelayCommand]
    private void DropText(string text) => SymbolsText = text;

    [RelayCommand]
    private void Clear()
    {
        SymbolsText = "";
        Details = null;
        Results.Results = [];
        Results.Message = null;
        ProgressPercent = 0;
        ProgressText = null;
        ElapsedText = null;
        InvalidSymbolsText = null;
        Message = null;
    }

    [RelayCommand]
    private void CopyRequestId()
    {
        if (ClipboardService.TryCopy(RequestId))
            Report(StatusLevel.Good, $"Request ID {RequestId} copied to the clipboard.");
    }

    private void Report(StatusLevel level, string message)
    {
        MessageLevel = level;
        Message = message;
    }

    internal static string? DescribeInvalid(IReadOnlyList<string> invalid)
    {
        if (invalid.Count == 0)
            return null;

        string shown = string.Join(", ", invalid.Take(InvalidSymbolsShown));
        string more = invalid.Count > InvalidSymbolsShown ? $" and {invalid.Count - InvalidSymbolsShown} more" : "";
        return invalid.Count == 1
            ? $"1 symbol isn't a valid Fyers symbol and wasn't fetched: {shown}."
            : $"{invalid.Count} symbols aren't valid Fyers symbols and weren't fetched: {shown}{more}.";
    }

    private void Show(QuoteRequestDetails details)
    {
        Details = details;
        Results.CreatedAt = details.CreatedAt;
        Results.Results = details.Results;
        int done = details.SucceededCount + details.FailedCount;
        ProgressPercent = details.TotalSymbols == 0 ? 100 : 100.0 * done / details.TotalSymbols;
        IsIndeterminate = !details.Status.IsTerminal() && done == 0;
        ElapsedText = details.StartedAt is { } started
            ? Display.Duration((details.CompletedAt ?? DateTimeOffset.UtcNow) - started)
            : null;
        ProgressText = details.Status.IsTerminal()
            ? $"Finished {Display.WhenInSentence(details.CompletedAt)}"
            : $"{done:N0} of {Display.Count(details.TotalSymbols, "symbol")} fetched";
        if (details.Error is { } error)
            Report(StatusLevel.Bad, error);
    }
}

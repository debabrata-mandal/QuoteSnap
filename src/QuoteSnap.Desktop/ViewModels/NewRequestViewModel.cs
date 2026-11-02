using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class NewRequestViewModel : ObservableObject
{
    public const string SearchHint = "Search stocks by company name, ticker or ISIN to add their Fyers symbols.";
    private const int InvalidSymbolsShown = 12;
    private const int MinSearchLength = 2;
    private const int SearchLimit = 20;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly QuoteSnapConnection _connection;
    private readonly FileDialogs _dialogs;
    private CancellationTokenSource? _search;

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

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<SymbolRow> SearchResults { get; set; } = [];

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    /// <summary>Whether the results dropdown is open; it opens as you type and closes on Esc or a click elsewhere.</summary>
    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string SearchEmptyText { get; set; } = SearchHint;

    /// <summary>How many Fyers symbols the service has and when they were downloaded, as in "18,432 Fyers symbols, downloaded today, 09:50".</summary>
    [ObservableProperty]
    public partial string SymbolStatusText { get; set; } = "";

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
        List<string> symbols = ListInput.Parse(value);
        SymbolCount = symbols.Count;
        LineCount = value.Split('\n').Count(line => line.Trim().Length > 0);
        HashSet<string> listed = new(symbols, StringComparer.OrdinalIgnoreCase);
        foreach (SymbolRow row in SearchResults)
            row.IsAdded = listed.Contains(row.Symbol);
    }

    /// <summary>Called when the page is opened.</summary>
    public Task ActivateAsync() => LoadSymbolStatusAsync();

    partial void OnSearchTextChanged(string value)
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;

        string query = value.Trim();
        if (query.Length < MinSearchLength)
        {
            IsSearching = false;
            SearchResults = [];
            SearchEmptyText = query.Length == 0 ? SearchHint : "Keep typing…";
            IsSearchOpen = query.Length > 0;
            return;
        }

        IsSearchOpen = true;
        _search = new CancellationTokenSource();
        _ = SearchAsync(query, _search.Token);
    }

    private async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDelay, cancellationToken);
            IsSearching = true;
            FyersSymbolSearchResult result = await _connection.Client.SearchSymbolsAsync(query, SearchLimit, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            HashSet<string> listed = new(ListInput.Parse(SymbolsText), StringComparer.OrdinalIgnoreCase);
            SearchResults = [.. result.Symbols.Select(s => new SymbolRow(s, listed.Contains(s.Symbol)))];
            SearchEmptyText = $"No listed stock matches “{query}”. Try part of the company name or the NSE ticker.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            SearchResults = [];
            SearchEmptyText = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }

        IsSearching = false;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
        IsSearchOpen = false;
    }

    /// <summary>Adds the stock's symbol to the end of the list, unless it's already there.</summary>
    [RelayCommand]
    private void AddSymbol(SymbolRow? row)
    {
        if (row is null)
            return;

        if (!ListInput.Parse(SymbolsText).Contains(row.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            string text = SymbolsText.TrimEnd();
            SymbolsText = text.Length == 0 ? row.Symbol : $"{text}{Environment.NewLine}{row.Symbol}";
        }

        row.IsAdded = true;
    }

    /// <summary>Enter in the search box adds the best match not yet on the list.</summary>
    [RelayCommand]
    private void AddTopResult() => AddSymbol(SearchResults.FirstOrDefault(r => !r.IsAdded));

    [RelayCommand]
    private async Task RefreshSymbolsAsync()
    {
        Message = null;
        try
        {
            FyersSymbolRefreshResult result = await _connection.Client.RefreshSymbolsAsync();
            ShowSymbolStatus(new FyersSymbolStatus(result.SymbolCount, result.LoadedAt));
            Report(StatusLevel.Good,
                $"Downloaded {Display.Count(result.SymbolCount, "Fyers symbol")}: {Display.Count(result.NseCount, "stock")} on NSE and {result.BseOnlyCount.ToString("N0", CultureInfo.CurrentCulture)} only on BSE. Fetch holdings again to apply them to fund holdings.");
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Couldn't refresh the Fyers symbols. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
    }

    private async Task LoadSymbolStatusAsync()
    {
        try
        {
            ShowSymbolStatus(await _connection.Client.GetSymbolStatusAsync());
        }
        catch (Exception)
        {
            SymbolStatusText = "Fyers symbols: unavailable";
        }
    }

    private void ShowSymbolStatus(FyersSymbolStatus status) => SymbolStatusText = status.LoadedAt is { } loadedAt
        ? $"{Display.Count(status.SymbolCount, "Fyers symbol")} for search and holdings, downloaded {Display.WhenInSentence(loadedAt)}"
        : "Fyers symbols not downloaded yet. Refresh them to search stocks by name and to fetch fund holdings.";

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
            Show(await _connection.WaitForCompletionAsync(accepted.RequestId, progress, cancellationToken));
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

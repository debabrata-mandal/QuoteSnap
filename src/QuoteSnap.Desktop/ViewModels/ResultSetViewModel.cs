using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>The quotes of one request, with a text filter, counts and CSV export; shared by New request and History.</summary>
public sealed partial class ResultSetViewModel(FileDialogs dialogs, string emptyText) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(Total), nameof(Ok), nameof(Errors), nameof(Invalid), nameof(Pending), nameof(RowCountText))]
    public partial IReadOnlyList<QuoteResultDto> Results { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowCountText))]
    public partial IReadOnlyList<QuoteResultDto> Visible { get; set; } = [];

    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>When the request was created; used in the export file name.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    public int Total => Results.Count;

    public int Ok => Results.Count(r => r.Status == QuoteResultStatus.Ok);

    public int Errors => Results.Count(r => r.Status == QuoteResultStatus.Error);

    public int Invalid => Results.Count(r => r.Status == QuoteResultStatus.Invalid);

    public int Pending => Results.Count(r => r.Status == QuoteResultStatus.Pending);

    public string RowCountText => Visible.Count == Results.Count
        ? Display.Count(Results.Count, "row")
        : $"{Visible.Count:N0} of {Display.Count(Results.Count, "row")}";

    public string EmptyText => Results.Count == 0 ? emptyText : $"No quotes match “{SearchText.Trim()}”.";

    partial void OnResultsChanged(IReadOnlyList<QuoteResultDto> value) => Refilter();

    partial void OnSearchTextChanged(string value) => Refilter();

    private bool CanExport() => Results.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        string name = $"quotes-{(CreatedAt ?? DateTimeOffset.Now).ToLocalTime():yyyyMMdd-HHmmss}.csv";
        if (dialogs.SaveCsv(name, CsvExport.Quotes(Results)) is { } path)
            Message = $"Saved {Display.Count(Results.Count, "row")} to {path}";
    }

    private void Refilter()
    {
        string term = SearchText.Trim();
        Visible = term.Length == 0
            ? Results
            : [.. Results.Where(r => r.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.Error?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || StatusText.Of(r.Status).Contains(term, StringComparison.OrdinalIgnoreCase))];
        OnPropertyChanged(nameof(EmptyText));
    }
}

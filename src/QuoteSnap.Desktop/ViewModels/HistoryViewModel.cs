using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    public const string AllFilter = "All";

    private const int PageSize = 50;

    private readonly QuoteSnapConnection _connection;
    private readonly SymbolExclusions _exclusions;
    private readonly List<RequestRow> _items = [];
    private int _detailsVersion;
    private Guid? _pendingSelection;

    public HistoryViewModel(QuoteSnapConnection connection, FileDialogs dialogs, SymbolExclusions exclusions)
    {
        _connection = connection;
        _exclusions = exclusions;
        _exclusions.Changed += (_, _) => OnExclusionsChanged();
        Results = new ResultSetViewModel(dialogs, "This request has no quotes.") { ProblemsFirst = true };
        Results.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ResultSetViewModel.Results))
                return;
            IsConfirmingExclude = false;
            OnExclusionsChanged();
        };
    }

    public ResultSetViewModel Results { get; }

    [ObservableProperty]
    public partial IReadOnlyList<RequestRow> VisibleItems { get; set; } = [];

    [ObservableProperty]
    public partial string StatusFilter { get; set; } = AllFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial DateTimeOffset? NextBefore { get; set; }

    [ObservableProperty]
    public partial RequestRow? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSummary))]
    public partial QuoteRequestDetails? SelectedDetails { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDetails { get; set; }

    [ObservableProperty]
    public partial string? CountText { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "Loading requests…";

    /// <summary>"Created today, 11:37:54 · took 0.4 s".</summary>
    public string? SelectedSummary => SelectedDetails is not { } d
        ? null
        : $"Created {Display.WhenInSentence(d.CreatedAt)} · took {Display.Duration(d.CompletedAt - d.StartedAt)}";

    partial void OnSelectedItemChanged(RequestRow? value) => _ = LoadDetailsAsync(value?.Id);

    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    /// <summary>Selects the request once the list is loaded, e.g. when it's opened from the dashboard; a filter, if given, is applied first.</summary>
    public async Task ShowAsync(Guid requestId, string? filter = null)
    {
        _pendingSelection = requestId;
        if (filter is not null)
            StatusFilter = filter;
        if (!RefreshCommand.IsRunning)
            await RefreshCommand.ExecuteAsync(null);
    }

    /// <summary>The selected request's symbols that Fyers answered with an error or rejected; waiting ones aren't counted.</summary>
    public IReadOnlyList<string> FailedSymbols => [.. Results.Results.Where(r => r.Status is QuoteResultStatus.Error or QuoteResultStatus.Invalid).Select(r => r.Symbol).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The failed symbols that aren't excluded for today yet.</summary>
    public IReadOnlyList<string> ExcludableSymbols => [.. FailedSymbols.Where(s => !_exclusions.Contains(s))];

    public bool CanExclude => ExcludableSymbols.Count > 0 && !IsConfirmingExclude;

    /// <summary>Every failed symbol is already excluded for today, so the "Include again" note shows instead.</summary>
    public bool AllFailedExcluded => FailedSymbols.Count > 0 && ExcludableSymbols.Count == 0;

    public string ExcludeText => $"Exclude {Display.Count(ExcludableSymbols.Count, "failed symbol")} for today";

    public string ExcludedText => $"{Display.Count(FailedSymbols.Count, "failed symbol")} excluded from holdings prices today";

    public string ConfirmExcludeText => $"Leave {string.Join(", ", ExcludableSymbols)} out of holdings live prices until tomorrow?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExclude))]
    [NotifyCanExecuteChangedFor(nameof(AskExcludeCommand))]
    public partial bool IsConfirmingExclude { get; set; }

    /// <summary>Asks first; nothing is excluded until the user confirms.</summary>
    [RelayCommand(CanExecute = nameof(CanExclude))]
    private void AskExclude() => IsConfirmingExclude = true;

    [RelayCommand]
    private async Task ConfirmExcludeAsync()
    {
        try
        {
            await _exclusions.ExcludeAsync(ExcludableSymbols);
            IsConfirmingExclude = false;
        }
        catch (Exception ex)
        {
            Message = "Couldn't exclude the symbols. " + ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    [RelayCommand]
    private void CancelExclude() => IsConfirmingExclude = false;

    [RelayCommand]
    private async Task IncludeAgainAsync()
    {
        try
        {
            await _exclusions.IncludeAsync(FailedSymbols);
        }
        catch (Exception ex)
        {
            Message = "Couldn't include the symbols again. " + ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    private void OnExclusionsChanged()
    {
        OnPropertyChanged(nameof(FailedSymbols));
        OnPropertyChanged(nameof(ExcludableSymbols));
        OnPropertyChanged(nameof(CanExclude));
        OnPropertyChanged(nameof(AllFailedExcluded));
        OnPropertyChanged(nameof(ExcludeText));
        OnPropertyChanged(nameof(ExcludedText));
        OnPropertyChanged(nameof(ConfirmExcludeText));
        AskExcludeCommand.NotifyCanExecuteChanged();
    }

    public async Task RefreshIfIdleAsync()
    {
        if (!RefreshCommand.IsRunning)
            await RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await _exclusions.RefreshAsync();
            QuoteRequestPage page = await _connection.Client.ListAsync(PageSize);
            _items.Clear();
            Append(page);
            Message = null;
        }
        catch (Exception ex)
        {
            Message = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
            EmptyText = "Couldn't load requests.";
        }

        Guid? wanted = _pendingSelection ?? SelectedItem?.Id ?? VisibleItems.FirstOrDefault()?.Id;
        _pendingSelection = null;
        if (wanted is { } id)
        {
            RequestRow? row = VisibleItems.FirstOrDefault(r => r.Id == id);
            if (row is null && StatusFilter != AllFilter)
            {
                StatusFilter = AllFilter;
                row = VisibleItems.FirstOrDefault(r => r.Id == id);
            }

            SelectedItem = row;
            if (row is null)
                await LoadDetailsAsync(id);
        }
    }

    private bool CanLoadMore() => NextBefore is not null;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        try
        {
            Append(await _connection.Client.ListAsync(PageSize, NextBefore));
        }
        catch (Exception ex)
        {
            Message = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    [RelayCommand]
    private void CopyRequestId()
    {
        if (ClipboardService.TryCopy(SelectedDetails?.RequestId.ToString()))
            Results.Message = "Request ID copied.";
    }

    private void Append(QuoteRequestPage page)
    {
        _items.AddRange(page.Items.Select(s => new RequestRow(s)));
        NextBefore = page.NextBefore;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Guid? selected = SelectedItem?.Id;
        VisibleItems = StatusFilter == AllFilter
            ? [.. _items]
            : [.. _items.Where(r => r.Summary.Status.ToString() == StatusFilter)];
        CountText = VisibleItems.Count == _items.Count
            ? Display.Count(_items.Count, "request") + (NextBefore is null ? "" : " loaded")
            : $"{VisibleItems.Count:N0} of {Display.Count(_items.Count, "request")}";
        EmptyText = _items.Count == 0 ? "No quote requests yet. Fetch quotes on the New request page." : "No requests match this filter.";
        if (selected is { } id)
            SelectedItem = VisibleItems.FirstOrDefault(r => r.Id == id);
    }

    private async Task LoadDetailsAsync(Guid? requestId)
    {
        int version = ++_detailsVersion;
        if (requestId is not { } id)
        {
            SelectedDetails = null;
            Results.Results = [];
            IsLoadingDetails = false;
            return;
        }

        if (SelectedDetails is { } current && current.RequestId == id && current.Status.IsTerminal())
            return;

        IsLoadingDetails = true;
        try
        {
            QuoteRequestDetails details = await _connection.Client.GetAsync(id);
            if (version != _detailsVersion)
                return;
            SelectedDetails = details;
            Results.CreatedAt = details.CreatedAt;
            Results.Message = null;
            Results.Results = details.Results;
        }
        catch (Exception ex)
        {
            if (version == _detailsVersion)
                Message = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
        finally
        {
            if (version == _detailsVersion)
                IsLoadingDetails = false;
        }
    }
}

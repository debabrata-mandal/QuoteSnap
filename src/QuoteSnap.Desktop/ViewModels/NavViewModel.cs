using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>The Mutual funds page: the user's saved fund list with latest NAVs, name search to add funds, and the AMFI file.</summary>
public sealed partial class NavViewModel : ObservableObject
{
    public const string SearchHint = "Search by fund name, fund house, ISIN or scheme code.";
    private const int MinSearchLength = 2;
    private const int SearchLimit = 25;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly QuoteSnapConnection _connection;
    private readonly FileDialogs _dialogs;
    private CancellationTokenSource? _search;
    private bool _myFundsLoaded;
    private NavEstimateList? _estimates;

    public NavViewModel(QuoteSnapConnection connection, FileDialogs dialogs)
    {
        _connection = connection;
        _dialogs = dialogs;
        MyFundsView = new ListCollectionView(MyFunds);
        MyFunds.CollectionChanged += (_, _) => OnMyFundsChanged();
        connection.EstimatesUpdated += ShowEstimates;
    }

    public event EventHandler? NavsRefreshed;

    /// <summary>Stored by the service, so every app pointed at it shares the list.</summary>
    public ObservableCollection<FundRow> MyFunds { get; } = [];

    /// <summary><see cref="MyFunds"/> in the chosen <see cref="Order"/>, re-sorted as estimates change.</summary>
    public ListCollectionView MyFundsView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReordered), nameof(EstimateHeaderText), nameof(CanReorder))]
    public partial FundOrder Order { get; private set; }

    public bool IsReordered => Order != FundOrder.Original;

    public string EstimateHeaderText => Order switch
    {
        FundOrder.HighestEstimateFirst => "EST. % ↓",
        FundOrder.LowestEstimateFirst => "EST. % ↑",
        _ => "EST. %",
    };

    /// <summary>Highest first, then lowest first, then back to the original order.</summary>
    [RelayCommand]
    private void SortByEstimate() => Order = Order switch
    {
        FundOrder.Original => FundOrder.HighestEstimateFirst,
        FundOrder.HighestEstimateFirst => FundOrder.LowestEstimateFirst,
        _ => FundOrder.Original,
    };

    [RelayCommand]
    private void ShowOriginalOrder() => Order = FundOrder.Original;

    partial void OnOrderChanged(FundOrder value) => MyFundsView.CustomSort = value == FundOrder.Original ? null : new FundEstimateComparer(MyFunds, value == FundOrder.HighestEstimateFirst);

    public bool HasFunds => MyFunds.Count > 0;

    /// <summary>Only once the list has loaded, so "empty" doesn't flash while it loads.</summary>
    public bool ShowEmptyState => _myFundsLoaded && MyFunds.Count == 0;

    public string MyFundsSummary => Display.Count(MyFunds.Count, "fund");

    [ObservableProperty]
    public partial string? AsOfText { get; set; }

    [ObservableProperty]
    public partial string? MyFundsError { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<FundRow> SearchResults { get; set; } = [];

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool SearchPartial { get; set; }

    [ObservableProperty]
    public partial string SearchEmptyText { get; set; } = SearchHint;

    [ObservableProperty]
    public partial StatusLevel StatusLevel { get; set; }

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = "Checking";

    [ObservableProperty]
    public partial string SchemeCountText { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string LoadedAtText { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string LatestNavDateText { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string? StatusError { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetPricesCommand))]
    public partial bool IsGettingPrices { get; set; }

    /// <summary>"Getting prices: 12 of 40 stocks", or when the prices behind the estimates were fetched.</summary>
    [ObservableProperty]
    public partial string? PricesText { get; set; }

    /// <summary>Called when the page is opened.</summary>
    public async Task ActivateAsync()
    {
        await LoadStatusAsync();
        await RefreshMyFundsAsync();
        await ResumePricesAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;

        string query = value.Trim();
        if (query.Length < MinSearchLength)
        {
            IsSearching = false;
            SearchPartial = false;
            SearchResults = [];
            SearchEmptyText = query.Length == 0 ? SearchHint : "Keep typing…";
            return;
        }

        _search = new CancellationTokenSource();
        _ = SearchAsync(query, _search.Token);
    }

    private async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDelay, cancellationToken);
            IsSearching = true;
            NavSearchResult result = await _connection.Client.SearchNavsAsync(query, SearchLimit, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            HashSet<int> added = [.. MyFunds.Select(f => f.SchemeCode)];
            SearchResults = [.. result.Funds.Select(f => FundRow.FromNav(f, added.Contains(f.SchemeCode)))];
            SearchPartial = result.Partial;
            SearchEmptyText = $"No funds match “{query}”. Try fewer or shorter words.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            SearchResults = [];
            SearchPartial = false;
            SearchEmptyText = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }

        IsSearching = false;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private async Task AddAsync(FundRow? row)
    {
        if (row is null || MyFunds.Any(f => f.SchemeCode == row.SchemeCode))
            return;

        try
        {
            MyFund added = await _connection.Client.AddMyFundAsync(row.SchemeCode);
            if (MyFunds.All(f => f.SchemeCode != added.SchemeCode))
                MyFunds.Add(FundRow.FromMyFund(added, navsLoaded: true));
            row.IsAdded = true;
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Couldn't add {row.Name}. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
    }

    /// <summary>The fund whose remove button was pressed, waiting for the user to confirm; null when nothing is being removed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingRemove), nameof(RemoveQuestion))]
    public partial FundRow? PendingRemoval { get; private set; }

    public bool IsConfirmingRemove => PendingRemoval is not null;

    public string RemoveQuestion => PendingRemoval is { } row ? $"Remove {row.Name}?" : "";

    [RelayCommand]
    private void AskRemove(FundRow? row) => PendingRemoval = row;

    [RelayCommand]
    private void KeepFund() => PendingRemoval = null;

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (PendingRemoval is not { } row)
            return;

        if (MyFunds.FirstOrDefault(f => f.SchemeCode == row.SchemeCode) is not { } existing)
        {
            PendingRemoval = null;
            return;
        }

        try
        {
            await _connection.Client.RemoveMyFundAsync(row.SchemeCode);
            MyFunds.Remove(existing);
            foreach (FundRow result in SearchResults.Where(r => r.SchemeCode == row.SchemeCode))
                result.IsAdded = false;
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Couldn't remove {row.Name}. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
        finally
        {
            PendingRemoval = null;
        }
    }

    /// <summary>Whether rows can be dragged: only in the user's own order, not while sorted by estimate.</summary>
    public bool CanReorder => !IsReordered && MyFunds.Count > 1;

    /// <summary>Moves <paramref name="row"/> to where <paramref name="target"/> is, as the user drags it; <see cref="SaveOrderAsync"/> keeps the result.</summary>
    public void MoveFund(FundRow row, FundRow target)
    {
        int from = MyFunds.IndexOf(row);
        int to = MyFunds.IndexOf(target);
        if (from >= 0 && to >= 0 && from != to)
            MyFunds.Move(from, to);
    }

    /// <summary>Saves the list's order after a drag, if it changed; reloads the saved order when the save fails.</summary>
    public async Task SaveOrderAsync(IReadOnlyList<int> before)
    {
        List<int> after = [.. MyFunds.Select(f => f.SchemeCode)];
        if (after.SequenceEqual(before))
            return;

        try
        {
            await _connection.Client.ReorderMyFundsAsync(after);
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Couldn't save the new order. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
            await RefreshMyFundsAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshMyFundsAsync()
    {
        try
        {
            MyFundList list = await _connection.Client.GetMyFundsAsync();
            List<FundRow> rows = [.. list.Funds.Select(f => FundRow.FromMyFund(f, navsLoaded: list.LoadedAt is not null))];
            MyFunds.Clear();
            foreach (FundRow row in rows)
                MyFunds.Add(row);
            _myFundsLoaded = true;
            OnMyFundsChanged();
            ApplyEstimates();

            int missing = rows.Count(r => r.IsMissing);
            MyFundsError = missing == 0 ? null
                : $"{Display.Count(missing, "fund")} on your list {(missing == 1 ? "is" : "are")} no longer in AMFI's file; the scheme may have been merged or closed.";
        }
        catch (Exception ex)
        {
            MyFundsError = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
            return;
        }

        await LoadEstimatesAsync();
    }

    private bool CanGetPrices() => !IsGettingPrices && MyFunds.Count > 0;

    /// <summary>Requests live prices for every stock the listed funds hold, then estimates each fund's NAV from them.</summary>
    [RelayCommand(CanExecute = nameof(CanGetPrices))]
    private async Task GetPricesAsync()
    {
        Message = null;
        QuoteRequestDetails started;
        try
        {
            QuoteRequestAccepted accepted = await _connection.Client.StartHoldingQuotesAsync();
            started = await _connection.Client.GetAsync(accepted.RequestId);
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Couldn't request live prices. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
            return;
        }

        await WatchPricesAsync(started);
    }

    /// <summary>Follows a live-prices request started elsewhere, such as on the Holdings page, that's still running.</summary>
    private async Task ResumePricesAsync()
    {
        if (IsGettingPrices)
            return;
        try
        {
            if (await _connection.Client.GetLatestHoldingQuotesAsync() is { } latest && !latest.Status.IsTerminal())
                await WatchPricesAsync(latest);
        }
        catch (Exception)
        {
            // The estimates already show the last finished prices; there's nothing to follow.
        }
    }

    private async Task WatchPricesAsync(QuoteRequestDetails request)
    {
        IsGettingPrices = true;
        PricesText = HoldingText.QuotesTitle(request);
        try
        {
            Progress<QuoteRequestDetails> progress = new(r => PricesText = HoldingText.QuotesTitle(r));
            QuoteRequestDetails finished = await _connection.WaitForCompletionAsync(request.RequestId, progress);
            if (finished.Status == QuoteRequestStatus.Failed)
                Report(StatusLevel.Bad, $"Couldn't get live prices. {finished.Error}");
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Lost track of the live prices; they may still be coming in the service. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
        finally
        {
            IsGettingPrices = false;
        }

        await LoadEstimatesAsync();
    }

    private async Task LoadEstimatesAsync()
    {
        NavEstimateList estimates;
        try
        {
            estimates = await _connection.Client.GetNavEstimatesAsync();
        }
        catch (Exception ex)
        {
            _estimates = null;
            if (!IsGettingPrices)
                PricesText = $"Couldn't load NAV estimates. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}";
            ApplyEstimates();
            return;
        }

        ShowEstimates(estimates);
    }

    /// <summary>Also called for every estimate the API pushes as live prices tick.</summary>
    private void ShowEstimates(NavEstimateList estimates)
    {
        _estimates = estimates;
        ApplyEstimates();
        if (!IsGettingPrices)
            PricesText = estimates.PricesAt is { } at ? $"Estimates use prices from {Display.WhenInSentence(at)}" : null;
    }

    private void ApplyEstimates()
    {
        Dictionary<int, NavEstimate> byScheme = _estimates?.Funds.ToDictionary(e => e.SchemeCode) ?? [];
        foreach (FundRow row in MyFunds)
            row.ShowEstimate(byScheme.GetValueOrDefault(row.SchemeCode), _estimates?.PricesAt);
        if (IsReordered)
            MyFundsView.Refresh();
    }

    [RelayCommand]
    private async Task LoadStatusAsync()
    {
        try
        {
            ShowStatus(await _connection.Client.GetNavStatusAsync());
            StatusError = null;
        }
        catch (Exception ex)
        {
            StatusLevel = StatusLevel.Bad;
            StatusSummary = "Unavailable";
            StatusError = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    [RelayCommand]
    private async Task RefreshNavsAsync()
    {
        Message = null;
        try
        {
            NavRefreshResult result = await _connection.Client.RefreshNavsAsync();
            ShowStatus(new NavStatus(result.SchemeCount, result.LatestNavDate, result.LoadedAt));
            StatusError = result.SkippedRows > 0 ? $"{Display.Count(result.SkippedRows, "row")} of the AMFI file couldn't be read and were skipped." : null;
            NavsRefreshed?.Invoke(this, EventArgs.Empty);
            await RefreshMyFundsAsync();
        }
        catch (Exception ex)
        {
            StatusError = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    private bool CanExport() => MyFunds.Any(f => f.Nav is not null);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        List<FundNav> navs = [.. MyFunds.Select(f => f.Nav).OfType<FundNav>()];
        if (_dialogs.SaveCsv($"my-funds-{DateTime.Now:yyyyMMdd-HHmmss}.csv", CsvExport.Navs(navs)) is { } path)
            Report(StatusLevel.Good, $"Saved {Display.Count(navs.Count, "fund")} to {path}");
    }

    private void OnMyFundsChanged()
    {
        OnPropertyChanged(nameof(HasFunds));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(MyFundsSummary));
        OnPropertyChanged(nameof(CanReorder));
        DateOnly? latest = MyFunds.Select(f => f.Nav?.NavDate).Max();
        AsOfText = latest is { } date ? $"NAVs as of {Display.Date(date)}" : null;
        ExportCommand.NotifyCanExecuteChanged();
        GetPricesCommand.NotifyCanExecuteChanged();
    }

    private void Report(StatusLevel level, string message)
    {
        MessageLevel = level;
        Message = message;
    }

    private void ShowStatus(NavStatus status)
    {
        SchemeCountText = status.SchemeCount.ToString("N0", CultureInfo.CurrentCulture);
        LatestNavDateText = Display.Date(status.LatestNavDate);
        if (status.LoadedAt is not { } loadedAt)
        {
            StatusLevel = StatusLevel.Warning;
            StatusSummary = "Not loaded";
            LoadedAtText = "Never";
            return;
        }

        bool stale = loadedAt.ToLocalTime().Date < DateTime.Today;
        StatusLevel = stale ? StatusLevel.Warning : StatusLevel.Good;
        StatusSummary = stale ? "Stale" : "Fresh";
        LoadedAtText = Display.When(loadedAt);
    }
}

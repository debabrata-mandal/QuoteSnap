using System.Collections.ObjectModel;
using System.Globalization;
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

    public NavViewModel(QuoteSnapConnection connection, FileDialogs dialogs)
    {
        _connection = connection;
        _dialogs = dialogs;
        MyFunds.CollectionChanged += (_, _) => OnMyFundsChanged();
    }

    public event EventHandler? NavsRefreshed;

    /// <summary>Stored by the service, so every app pointed at it shares the list.</summary>
    public ObservableCollection<FundRow> MyFunds { get; } = [];

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

    /// <summary>Called when the page is opened.</summary>
    public async Task ActivateAsync()
    {
        await LoadStatusAsync();
        await RefreshMyFundsAsync();
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

    [RelayCommand]
    private async Task RemoveAsync(FundRow? row)
    {
        if (row is null || MyFunds.FirstOrDefault(f => f.SchemeCode == row.SchemeCode) is not { } existing)
            return;

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

            int missing = rows.Count(r => r.IsMissing);
            MyFundsError = missing == 0 ? null
                : $"{Display.Count(missing, "fund")} on your list {(missing == 1 ? "is" : "are")} no longer in AMFI's file; the scheme may have been merged or closed.";
        }
        catch (Exception ex)
        {
            MyFundsError = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
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
        DateOnly? latest = MyFunds.Select(f => f.Nav?.NavDate).Max();
        AsOfText = latest is { } date ? $"NAVs as of {Display.Date(date)}" : null;
        ExportCommand.NotifyCanExecuteChanged();
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

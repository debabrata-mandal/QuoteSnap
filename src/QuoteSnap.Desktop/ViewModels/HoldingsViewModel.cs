using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>
/// The Holdings page: the listed funds, the selected fund's portfolio month by month, and fetching every fund's
/// holdings from ScanX with live progress.
/// </summary>
public sealed partial class HoldingsViewModel : ObservableObject
{
    private readonly QuoteSnapConnection _connection;
    private readonly FileDialogs _dialogs;
    private IReadOnlyList<HoldingRow> _allHoldings = [];
    private Dictionary<string, QuoteResultDto> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveQuote> _live = new(StringComparer.OrdinalIgnoreCase);
    private Guid? _watching;
    private Guid? _watchingQuotes;
    private int _loadVersion;

    public HoldingsViewModel(QuoteSnapConnection connection, FileDialogs dialogs)
    {
        _connection = connection;
        _dialogs = dialogs;
        Funds.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFunds));
            OnPropertyChanged(nameof(ShowNoFunds));
            OnPropertyChanged(nameof(FundsSummary));
            FetchCommand.NotifyCanExecuteChanged();
            GetPricesCommand.NotifyCanExecuteChanged();
        };
        connection.LiveQuotesUpdated += ShowLiveQuotes;
        connection.Changed += (_, _) => _live.Clear();
    }

    /// <summary>Raised to open the Mutual funds page, where funds are added.</summary>
    public event EventHandler? AddFundsRequested;

    public ObservableCollection<HoldingsFundRow> Funds { get; } = [];

    public bool HasFunds => Funds.Count > 0;

    public bool ShowNoFunds => FundsLoaded && Funds.Count == 0;

    public string FundsSummary => Display.Count(Funds.Count, "fund");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoFunds))]
    public partial bool FundsLoaded { get; set; }

    [ObservableProperty]
    public partial string? FundsError { get; set; }

    [ObservableProperty]
    public partial HoldingsFundRow? SelectedFund { get; set; }

    /// <summary>The portfolio shown, or null while none is loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPortfolio), nameof(MonthText), nameof(MatchedText), nameof(FetchedText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousMonthCommand), nameof(NextMonthCommand), nameof(ExportCommand))]
    public partial FundHoldings? Portfolio { get; set; }

    public bool HasPortfolio => Portfolio is not null;

    public string MonthText => Portfolio is { } p ? HoldingText.Month(p.Month) : Display.Missing;

    public string? MatchedText => Portfolio is { } p ? $"ScanX scheme: {p.ScanxScheme}" : null;

    public string? FetchedText => Portfolio is { } p ? $"Fetched {Display.WhenInSentence(p.FetchedAt)}" : null;

    [ObservableProperty]
    public partial IReadOnlyList<HoldingRow> Holdings { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<AssetShare> AssetMix { get; set; } = [];

    [ObservableProperty]
    public partial string HoldingsCountText { get; set; } = "";

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoadingHoldings { get; set; }

    [ObservableProperty]
    public partial string HoldingsEmptyText { get; set; } = "Select a fund to see what it holds.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    public partial bool IsFetching { get; set; }

    /// <summary>The fetch shown in the progress card: the running one, or the last one until it's dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFetchCard))]
    public partial HoldingFetch? Fetch { get; set; }

    public bool ShowFetchCard => Fetch is not null;

    [ObservableProperty]
    public partial string FetchTitle { get; set; } = "";

    [ObservableProperty]
    public partial string FetchDetail { get; set; } = "";

    [ObservableProperty]
    public partial double FetchPercent { get; set; }

    [ObservableProperty]
    public partial StatusLevel FetchLevel { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GetPricesCommand))]
    public partial bool IsGettingPrices { get; set; }

    /// <summary>The last live-prices request for the holdings' stocks, running or finished.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPricesCard), nameof(PricesRequestId))]
    public partial QuoteRequestDetails? Prices { get; set; }

    public bool ShowPricesCard => Prices is not null;

    /// <summary>"Request 4b1f09c2", short like the History page.</summary>
    public string? PricesRequestId => Prices is { } p ? Display.ShortId(p.RequestId) : null;

    [ObservableProperty]
    public partial string PricesTitle { get; set; } = "";

    [ObservableProperty]
    public partial string PricesDetail { get; set; } = "";

    [ObservableProperty]
    public partial double PricesPercent { get; set; }

    [ObservableProperty]
    public partial StatusLevel PricesLevel { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    /// <summary>Called when the page is opened.</summary>
    public async Task ActivateAsync()
    {
        await LoadFundsAsync();
        await LoadLatestPricesAsync();
        await LoadLivePricesAsync();
        await ResumeRunningFetchAsync();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void AddFunds() => AddFundsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task SelectAsync(HoldingsFundRow? row)
    {
        if (row is null)
            return;

        foreach (HoldingsFundRow fund in Funds)
            fund.IsSelected = fund == row;
        SelectedFund = row;
        await LoadHoldingsAsync(row, month: null);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadFundsAsync();
        await LoadLatestPricesAsync();
        await LoadLivePricesAsync();
        await ResumeRunningFetchAsync();
    }

    private bool CanGetPrices() => !IsGettingPrices && Funds.Count > 0;

    /// <summary>Requests quotes for every distinct stock held by the listed funds, then shows each one's LTP.</summary>
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

    [RelayCommand]
    private void CopyPricesRequestId()
    {
        if (ClipboardService.TryCopy(Prices?.RequestId.ToString()))
            Report(StatusLevel.Good, "Request ID copied.");
    }

    private bool CanFetch() => !IsFetching && Funds.Count > 0;

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        Message = null;
        try
        {
            HoldingFetch started = await _connection.Client.StartHoldingFetchAsync();
            await WatchAsync(started);
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"Couldn't start fetching holdings. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
    }

    [RelayCommand]
    private void DismissFetch()
    {
        if (IsFetching)
            return;
        Fetch = null;
        foreach (HoldingsFundRow fund in Funds)
            fund.FetchStatus = null;
    }

    private bool CanGoBack() => Portfolio is { } p && p.Months.Any(m => m < p.Month);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousMonthAsync() => ShowMonthAsync(older: true);

    private bool CanGoForward() => Portfolio is { } p && p.Months.Any(m => m > p.Month);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextMonthAsync() => ShowMonthAsync(older: false);

    private bool CanExport() => Portfolio is { Holdings.Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (Portfolio is not { } p)
            return;
        string name = $"holdings-{p.SchemeCode}-{p.Month:yyyy-MM}.csv";
        if (_dialogs.SaveCsv(name, CsvExport.Holdings(p)) is { } path)
            Report(StatusLevel.Good, $"Saved {Display.Count(p.Holdings.Count, "holding")} of {p.SchemeName} to {path}");
    }

    private Task ShowMonthAsync(bool older)
    {
        if (Portfolio is not { } p || SelectedFund is not { } fund)
            return Task.CompletedTask;
        DateOnly? month = older ? p.Months.Where(m => m < p.Month).Cast<DateOnly?>().Max() : p.Months.Where(m => m > p.Month).Cast<DateOnly?>().Min();
        return month is { } target ? LoadHoldingsAsync(fund, target) : Task.CompletedTask;
    }

    private async Task LoadFundsAsync()
    {
        try
        {
            MyFundList list = await _connection.Client.GetMyFundsAsync();
            int? selected = SelectedFund?.SchemeCode;
            Dictionary<int, HoldingFetchFundStatus?> statuses = Funds.ToDictionary(f => f.SchemeCode, f => f.FetchStatus);
            Funds.Clear();
            foreach (MyFund fund in list.Funds)
                Funds.Add(new HoldingsFundRow(fund) { FetchStatus = statuses.GetValueOrDefault(fund.SchemeCode) });
            FundsError = null;
            FundsLoaded = true;

            HoldingsFundRow? next = Funds.FirstOrDefault(f => f.SchemeCode == selected)
                ?? Funds.FirstOrDefault(f => f.HasHoldings)
                ?? Funds.FirstOrDefault();
            if (next is null)
            {
                SelectedFund = null;
                ShowPortfolio(null, "Add funds on the Mutual funds page, then fetch their holdings.");
                return;
            }

            await SelectAsync(next);
        }
        catch (Exception ex)
        {
            FundsError = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    private async Task LoadHoldingsAsync(HoldingsFundRow fund, DateOnly? month)
    {
        int version = ++_loadVersion;
        if (!fund.HasHoldings)
        {
            ShowPortfolio(null, IsFetching
                ? "Its holdings will appear here once they're fetched."
                : "No holdings yet. Press Fetch holdings to scrape them from ScanX.");
            return;
        }

        IsLoadingHoldings = true;
        try
        {
            FundHoldings portfolio = await _connection.Client.GetFundHoldingsAsync(fund.SchemeCode, month);
            if (version == _loadVersion)
                ShowPortfolio(portfolio, "No holdings match the filter.");
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                ShowPortfolio(null, ErrorText.Describe(ex, _connection.Settings.BaseAddress));
        }
        finally
        {
            if (version == _loadVersion)
                IsLoadingHoldings = false;
        }
    }

    private void ShowPortfolio(FundHoldings? portfolio, string emptyText)
    {
        Portfolio = portfolio;
        HoldingsEmptyText = emptyText;
        if (portfolio is null)
        {
            _allHoldings = [];
            AssetMix = [];
            HoldingsCountText = "";
            ApplyFilter();
            return;
        }

        bool previousStored = portfolio.Months.Contains(portfolio.Month.AddMonths(-1));
        decimal largest = portfolio.Holdings.Count == 0 ? 0 : portfolio.Holdings.Max(h => h.Percent);
        _allHoldings = [.. portfolio.Holdings.Select(h => new HoldingRow(
            h, largest, previousStored, h.Symbol is { } symbol ? _quotes.GetValueOrDefault(symbol) : null, h.Symbol is { } live ? _live.GetValueOrDefault(live) : null))];
        AssetMix = [.. HoldingText.AssetMix(portfolio.Holdings).Select(t => new AssetShare(t.Label, HoldingText.Weight(t.Percent)))];
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string filter = FilterText.Trim();
        Holdings = filter.Length == 0 ? _allHoldings : [.. _allHoldings.Where(h => h.Matches(filter))];
        HoldingsCountText = Holdings.Count == _allHoldings.Count
            ? Display.Count(_allHoldings.Count, "holding")
            : $"{Holdings.Count} of {_allHoldings.Count}";
    }

    /// <summary>Re-prices the rows of the stocks that ticked, in place, so the table keeps its scroll and selection.</summary>
    private void ShowLiveQuotes(LiveQuoteList batch)
    {
        Dictionary<string, LiveQuote> ticked = batch.Quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
        foreach (LiveQuote quote in batch.Quotes)
            _live[quote.Symbol] = quote;
        foreach (HoldingRow row in _allHoldings)
        {
            if (row.Symbol is { } symbol && ticked.TryGetValue(symbol, out LiveQuote? quote))
                row.ApplyLive(quote);
        }
    }

    /// <summary>The streamed prices so far, for rows built before the next tick; pushes keep them current after this.</summary>
    private async Task LoadLivePricesAsync()
    {
        try
        {
            ShowLiveQuotes(await _connection.Client.GetLiveQuotesAsync());
        }
        catch (Exception)
        {
            // The table still has the last live-prices request; the feed is optional.
        }
    }

    /// <summary>Shows the last live-prices request, and follows it when it's still running.</summary>
    private async Task LoadLatestPricesAsync()
    {
        if (IsGettingPrices)
            return;
        try
        {
            if (await _connection.Client.GetLatestHoldingQuotesAsync() is not { } latest)
                return;
            if (latest.Status.IsTerminal())
                ShowPrices(latest);
            else
                _ = WatchPricesAsync(latest);
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Warning, $"Couldn't load the last live prices. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
    }

    private async Task WatchPricesAsync(QuoteRequestDetails request)
    {
        if (_watchingQuotes == request.RequestId)
            return;

        _watchingQuotes = request.RequestId;
        IsGettingPrices = true;
        ShowPrices(request);
        try
        {
            Progress<QuoteRequestDetails> progress = new(ShowPrices);
            QuoteRequestDetails finished = await _connection.WaitForCompletionAsync(request.RequestId, progress);
            ShowPrices(finished);
        }
        catch (Exception ex)
        {
            PricesLevel = StatusLevel.Bad;
            PricesDetail = $"Lost track of the request; it may still be running in the service. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}";
        }
        finally
        {
            _watchingQuotes = null;
            IsGettingPrices = false;
        }
    }

    /// <summary>Updates the progress card; once the request has finished, puts its prices in the table.</summary>
    private void ShowPrices(QuoteRequestDetails request)
    {
        Prices = request;
        PricesTitle = HoldingText.QuotesTitle(request);
        PricesDetail = HoldingText.QuotesDetail(request);
        PricesPercent = HoldingText.QuotesPercent(request);
        PricesLevel = request.Status switch
        {
            QuoteRequestStatus.Completed => StatusLevel.Good,
            QuoteRequestStatus.CompletedWithErrors => StatusLevel.Warning,
            QuoteRequestStatus.Failed => StatusLevel.Bad,
            _ => StatusLevel.Busy,
        };

        if (!request.Status.IsTerminal())
            return;
        _quotes = request.Results
            .GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (Portfolio is { } portfolio)
            ShowPortfolio(portfolio, HoldingsEmptyText);
    }

    /// <summary>Shows the progress of a fetch started elsewhere, or before the app was reopened.</summary>
    private async Task ResumeRunningFetchAsync()
    {
        if (IsFetching)
            return;
        try
        {
            if (await _connection.Client.GetLatestHoldingFetchAsync() is { } latest && !latest.Status.IsTerminal())
                await WatchAsync(latest);
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Warning, $"Couldn't check for a running holdings fetch. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
    }

    private async Task WatchAsync(HoldingFetch fetch)
    {
        if (_watching == fetch.Id)
            return;

        _watching = fetch.Id;
        IsFetching = true;
        ShowProgress(fetch);
        try
        {
            Progress<HoldingFetch> progress = new(ShowProgress);
            HoldingFetch finished = await _connection.WaitForHoldingFetchAsync(fetch.Id, progress);
            ShowProgress(finished);
        }
        catch (Exception ex)
        {
            FetchLevel = StatusLevel.Bad;
            FetchDetail = $"Lost track of the fetch; it may still be running in the service. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}";
        }
        finally
        {
            _watching = null;
            IsFetching = false;
        }

        await LoadFundsAsync();
    }

    private void ShowProgress(HoldingFetch fetch)
    {
        HoldingFetch? before = Fetch;
        Fetch = fetch;
        FetchTitle = HoldingText.FetchTitle(fetch);
        FetchDetail = HoldingText.FetchDetail(fetch);
        FetchPercent = HoldingText.FetchPercent(fetch);
        FetchLevel = fetch.Status switch
        {
            HoldingFetchStatus.Completed => StatusLevel.Good,
            HoldingFetchStatus.CompletedWithErrors => StatusLevel.Warning,
            HoldingFetchStatus.Failed => StatusLevel.Bad,
            _ => StatusLevel.Busy,
        };

        foreach (HoldingFetchFund progress in fetch.Funds)
        {
            if (Funds.FirstOrDefault(f => f.SchemeCode == progress.SchemeCode) is not { } row)
                continue;
            row.FetchStatus = progress.Status;
            row.FetchError = progress.Error;

            bool justFinished = progress.Status == HoldingFetchFundStatus.Ok
                && before?.Funds.FirstOrDefault(f => f.SchemeCode == progress.SchemeCode)?.Status != HoldingFetchFundStatus.Ok;
            if (justFinished && progress is { ScanxScheme: { } scheme, Month: { } month, HoldingCount: { } count })
            {
                row.Summary = new HoldingsSummary(scheme, month, count, DateTimeOffset.UtcNow);
                if (row == SelectedFund)
                    _ = LoadHoldingsAsync(row, month: null);
            }
        }
    }

    private void Report(StatusLevel level, string message)
    {
        MessageLevel = level;
        Message = message;
    }
}

/// <summary>An asset type's share of the portfolio, as in "Equity 84.17%".</summary>
public sealed record AssetShare(string Label, string PercentText);

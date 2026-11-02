using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>Every listed stock the user's funds hold, by weight averaged over the funds, with prices streamed in as they tick.</summary>
public sealed partial class StocksViewModel : ObservableObject
{
    private readonly QuoteSnapConnection _connection;
    private TopHoldingList? _list;
    private bool _activated;
    private bool _refreshAgain;

    public StocksViewModel(QuoteSnapConnection connection)
    {
        _connection = connection;
        StocksView = new ListCollectionView(Stocks) { Filter = Matches };
        connection.Changed += async (_, _) => await RefreshIfActivatedAsync();
        connection.HubConnected += async () => await RefreshIfActivatedAsync();
        connection.ServerChanged += async _ => await RefreshIfActivatedAsync();
        connection.QuoteRequestUpdated += async r =>
        {
            if (r.Status.IsTerminal())
                await RefreshIfActivatedAsync();
        };
        connection.LiveQuotesUpdated += ShowLiveQuotes;
    }

    /// <summary>Largest weight first; rows are replaced in place as prices tick, so the scroll position stays.</summary>
    public ObservableCollection<TopHoldingRow> Stocks { get; } = [];

    /// <summary><see cref="Stocks"/> narrowed by <see cref="FilterText"/>.</summary>
    public ListCollectionView StocksView { get; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    /// <summary>"312 stocks", or "12 of 312 stocks" while filtered.</summary>
    [ObservableProperty]
    public partial string? Summary { get; private set; }

    /// <summary>"Average weight across 11 funds · prices from today 10:42".</summary>
    [ObservableProperty]
    public partial string? Caption { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial string EmptyText { get; private set; } = "Loading…";

    public bool HasStocks => Stocks.Count > 0;

    public bool ShowEmptyState => !HasStocks || StocksView.IsEmpty;

    public async Task ActivateAsync()
    {
        _activated = true;
        await RefreshIfIdleAsync();
    }

    /// <summary>Refreshes, or refreshes once more after the one already running, so no change is missed.</summary>
    public async Task RefreshIfIdleAsync()
    {
        if (RefreshCommand.IsRunning)
        {
            _refreshAgain = true;
            return;
        }

        do
        {
            _refreshAgain = false;
            await RefreshCommand.ExecuteAsync(null);
        }
        while (_refreshAgain);
    }

    private Task RefreshIfActivatedAsync() => _activated ? RefreshIfIdleAsync() : Task.CompletedTask;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            Show(await _connection.Client.GetTopHoldingsAsync(limit: null));
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
            if (_list is null)
                EmptyText = "Stocks appear here once the API responds.";
        }
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    partial void OnFilterTextChanged(string value)
    {
        StocksView.Refresh();
        OnListChanged();
    }

    private void Show(TopHoldingList list)
    {
        _list = list;
        decimal largest = list.Holdings.Count > 0 ? list.Holdings.Max(h => h.Percent) : 0;
        Stocks.Clear();
        for (int i = 0; i < list.Holdings.Count; i++)
            Stocks.Add(new TopHoldingRow(i + 1, list.Holdings[i], largest, list.FundCount));
        Caption = DashboardViewModel.TopHoldingsText(list);
        EmptyText = list.FundCount == 0 ? "No holdings yet. Add funds on the Mutual funds page and fetch their ScanX holdings on the Holdings page." : "None of your funds' holdings are listed stocks.";
        OnListChanged();
    }

    private void ShowLiveQuotes(LiveQuoteList batch)
    {
        if (_list is null || Stocks.Count == 0)
            return;

        Dictionary<string, LiveQuote> quotes = batch.Quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        for (int i = 0; i < Stocks.Count; i++)
        {
            if (quotes.TryGetValue(Stocks[i].Symbol, out LiveQuote? quote))
            {
                Stocks[i] = Stocks[i].WithPrice(quote);
                changed = true;
            }
        }

        if (!changed)
            return;

        DateTimeOffset latest = quotes.Values.Max(q => q.UpdatedAt);
        if (_list.PricesAt is null || latest > _list.PricesAt)
            _list = _list with { PricesAt = latest };
        Caption = DashboardViewModel.TopHoldingsText(_list);
    }

    private bool Matches(object item)
    {
        string filter = FilterText.Trim();
        return filter.Length == 0 || item is TopHoldingRow row && row.Tip.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnListChanged()
    {
        int shown = StocksView.Count;
        Summary = Stocks.Count == 0 ? null : shown == Stocks.Count ? Display.Count(Stocks.Count, "stock") : $"{shown:N0} of {Display.Count(Stocks.Count, "stock")}";
        if (Stocks.Count > 0 && shown == 0)
            EmptyText = $"No stock matches “{FilterText.Trim()}”.";
        else if (Stocks.Count > 0)
            EmptyText = "";
        OnPropertyChanged(nameof(HasStocks));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
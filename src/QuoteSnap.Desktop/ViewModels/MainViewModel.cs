using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Settings;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const int DashboardTab = 0;
    public const int NewRequestTab = 1;
    public const int HistoryTab = 2;
    public const int NavTab = 3;
    public const int SettingsTab = 4;
    public const int HoldingsTab = 5;
    public const int FyersLoginTab = 6;
    public const int StocksTab = 7;

    private readonly QuoteSnapConnection _connection;

    public MainViewModel(SettingsStore store, QuoteSnapConnection connection, LoadedSettings loaded)
    {
        _connection = connection;
        FileDialogs dialogs = new();
        Dashboard = new DashboardViewModel(connection);
        NewRequest = new NewRequestViewModel(connection, dialogs);
        History = new HistoryViewModel(connection, dialogs);
        Nav = new NavViewModel(connection, dialogs);
        Holdings = new HoldingsViewModel(connection, dialogs);
        Stocks = new StocksViewModel(connection);
        Settings = new SettingsViewModel(store, connection, loaded);
        FyersLogin = new FyersLoginViewModel(connection, store);

        Holdings.AddFundsRequested += (_, _) => SelectedTabIndex = NavTab;
        FyersLogin.SettingsRequested += (_, _) => SelectedTabIndex = SettingsTab;
        FyersLogin.SessionChanged += async (_, _) => await Dashboard.RefreshIfIdleAsync();

        NewRequest.RequestFinished += async (_, _) => await Dashboard.RefreshIfIdleAsync();
        Nav.NavsRefreshed += async (_, _) => await Dashboard.RefreshIfIdleAsync();
        connection.Changed += (_, _) => OnPropertyChanged(nameof(Title));
    }

    public string Title => $"QuoteSnap — {_connection.Settings.BaseAddress.Authority}";

    public DashboardViewModel Dashboard { get; }

    public NewRequestViewModel NewRequest { get; }

    public HistoryViewModel History { get; }

    public NavViewModel Nav { get; }

    public HoldingsViewModel Holdings { get; }

    public StocksViewModel Stocks { get; }

    public FyersLoginViewModel FyersLogin { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    public void Start()
    {
        Dashboard.Start();
        _connection.Start();
    }

    [RelayCommand]
    private void GoTo(string tab) => SelectedTabIndex = int.Parse(tab, System.Globalization.CultureInfo.InvariantCulture);

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == HistoryTab)
            _ = History.RefreshIfIdleAsync();
        else if (value == NewRequestTab)
            _ = NewRequest.ActivateAsync();
        else if (value == NavTab)
            _ = Nav.ActivateAsync();
        else if (value == HoldingsTab)
            _ = Holdings.ActivateAsync();
        else if (value == StocksTab)
            _ = Stocks.ActivateAsync();
        else if (value == FyersLoginTab)
            _ = FyersLogin.ActivateAsync();
    }
}

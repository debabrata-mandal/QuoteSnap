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
        Settings = new SettingsViewModel(store, connection, loaded);
        FyersLogin = new FyersLoginViewModel(connection, store);

        Holdings.AddFundsRequested += (_, _) => SelectedTabIndex = NavTab;
        FyersLogin.SettingsRequested += (_, _) => SelectedTabIndex = SettingsTab;
        FyersLogin.LoggedIn += async (_, _) => await Dashboard.RefreshIfIdleAsync();

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

    public FyersLoginViewModel FyersLogin { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>The row clicked in the dashboard's recent requests; opens it on History and then clears, so the same row can be clicked again.</summary>
    [ObservableProperty]
    public partial RequestRow? RecentSelection { get; set; }

    partial void OnRecentSelectionChanged(RequestRow? value)
    {
        if (value is null)
            return;

        _ = OpenRequestAsync(value);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() => RecentSelection = null);
    }

    public void Start() => Dashboard.Start();

    [RelayCommand]
    private void GoTo(string tab) => SelectedTabIndex = int.Parse(tab, System.Globalization.CultureInfo.InvariantCulture);

    [RelayCommand]
    private async Task OpenRequestAsync(RequestRow? row)
    {
        if (row is null)
            return;

        SelectedTabIndex = HistoryTab;
        await History.ShowAsync(row.Id);
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == HistoryTab)
            _ = History.RefreshIfIdleAsync();
        else if (value == NavTab)
            _ = Nav.ActivateAsync();
        else if (value == HoldingsTab)
            _ = Holdings.ActivateAsync();
        else if (value == FyersLoginTab)
            _ = FyersLogin.ActivateAsync();
    }
}

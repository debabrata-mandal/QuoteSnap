using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

public sealed partial class NavViewModel(QuoteSnapConnection connection, FileDialogs dialogs) : ObservableObject
{
    public event EventHandler? NavsRefreshed;

    [ObservableProperty]
    public partial string IsinText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookupCommand))]
    [NotifyPropertyChangedFor(nameof(IsinSummary))]
    public partial int IsinCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    public partial IReadOnlyList<FundNav> Funds { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    public partial IReadOnlyList<string> NotFound { get; set; } = [];

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
    public partial string EmptyText { get; set; } = "Enter ISINs and look them up to see their latest NAVs.";

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    public string IsinSummary => Display.Count(IsinCount, "ISIN");

    public string? ResultSummary => Funds.Count == 0 && NotFound.Count == 0
        ? null
        : $"{Display.Count(Funds.Count, "scheme")} found" + (NotFound.Count > 0 ? $" · {NotFound.Count:N0} not found" : "");

    partial void OnIsinTextChanged(string value) => IsinCount = ListInput.Parse(value, upperCase: true).Count;

    [RelayCommand]
    private async Task LoadStatusAsync()
    {
        try
        {
            ShowStatus(await connection.Client.GetNavStatusAsync());
            StatusError = null;
        }
        catch (Exception ex)
        {
            StatusLevel = StatusLevel.Bad;
            StatusSummary = "Unavailable";
            StatusError = ErrorText.Describe(ex, connection.Settings.BaseAddress);
        }
    }

    private bool CanLookup() => IsinCount > 0;

    [RelayCommand(CanExecute = nameof(CanLookup))]
    private async Task LookupAsync()
    {
        Message = null;
        try
        {
            NavLookup lookup = await connection.Client.GetNavsAsync(ListInput.Parse(IsinText, upperCase: true));
            Funds = lookup.Funds;
            NotFound = lookup.NotFound;
            EmptyText = "None of these ISINs matched a Direct Plan scheme.";
        }
        catch (Exception ex)
        {
            Funds = [];
            NotFound = [];
            EmptyText = "The lookup failed.";
            Report(StatusLevel.Bad, ErrorText.Describe(ex, connection.Settings.BaseAddress));
        }
    }

    [RelayCommand]
    private void DropText(string text) => IsinText = text;

    [RelayCommand]
    private void LoadFile()
    {
        if (dialogs.OpenText("Load ISINs") is { } text)
            IsinText = text;
    }

    [RelayCommand]
    private void Clear()
    {
        IsinText = "";
        Funds = [];
        NotFound = [];
        Message = null;
        EmptyText = "Enter ISINs and look them up to see their latest NAVs.";
    }

    [RelayCommand]
    private async Task RefreshNavsAsync()
    {
        Message = null;
        try
        {
            NavRefreshResult result = await connection.Client.RefreshNavsAsync();
            ShowStatus(new NavStatus(result.SchemeCount, result.LatestNavDate, result.LoadedAt));
            StatusError = result.SkippedRows > 0 ? $"{Display.Count(result.SkippedRows, "row")} of the AMFI file couldn't be read and were skipped." : null;
            NavsRefreshed?.Invoke(this, EventArgs.Empty);
            if (IsinCount > 0)
                await LookupAsync();
        }
        catch (Exception ex)
        {
            StatusError = ErrorText.Describe(ex, connection.Settings.BaseAddress);
        }
    }

    private bool CanExport() => Funds.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        if (dialogs.SaveCsv($"navs-{DateTime.Now:yyyyMMdd-HHmmss}.csv", CsvExport.Navs(Funds)) is { } path)
            Report(StatusLevel.Good, $"Saved {Display.Count(Funds.Count, "row")} to {path}");
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

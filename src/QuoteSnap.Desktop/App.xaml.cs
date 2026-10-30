using System.Windows;
using System.Windows.Threading;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Settings;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop;

public partial class App : Application
{
    private QuoteSnapConnection? _connection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        SettingsStore store = SettingsStore.CreateDefault();
        LoadedSettings loaded = store.Load();
        _connection = new QuoteSnapConnection(loaded.Settings);
        MainViewModel main = new(store, _connection, loaded);

        MainWindow window = new() { DataContext = main };
        MainWindow = window;
        window.Show();
        main.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _connection?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (MainWindow is { IsLoaded: true } owner)
            MessageBox.Show(owner, e.Exception.Message, "QuoteSnap", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(e.Exception.Message, "QuoteSnap", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

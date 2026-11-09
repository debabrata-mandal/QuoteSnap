using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Views;

/// <summary>PasswordBox.Password can't be bound, so it's synced with the view model here.</summary>
public partial class ZerodhaView : UserControl
{
    private ZerodhaViewModel? _viewModel;

    public ZerodhaView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.BrowserLoginEnded -= OnBrowserLoginEnded;
        }

        _viewModel = e.NewValue as ZerodhaViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.BrowserLoginEnded += OnBrowserLoginEnded;
            ApiSecretBox.Password = _viewModel.ApiSecret;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is not null && e.PropertyName == nameof(ZerodhaViewModel.ApiSecret) && ApiSecretBox.Password != _viewModel.ApiSecret)
            ApiSecretBox.Password = _viewModel.ApiSecret;
    }

    /// <summary>The browser has the focus at this point; Windows only lets a window take it back while it's briefly topmost.</summary>
    private void OnBrowserLoginEnded(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this) is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.ApiSecret = ApiSecretBox.Password;
    }
}

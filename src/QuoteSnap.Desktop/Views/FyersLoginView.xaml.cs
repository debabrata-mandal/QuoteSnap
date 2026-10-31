using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Views;

/// <summary>PasswordBox.Password can't be bound, so it's synced with the view model here.</summary>
public partial class FyersLoginView : UserControl
{
    private FyersLoginViewModel? _viewModel;

    public FyersLoginView()
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

        _viewModel = e.NewValue as FyersLoginViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.BrowserLoginEnded += OnBrowserLoginEnded;
            SecretIdBox.Password = _viewModel.SecretId;
            PinBox.Password = _viewModel.Pin;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;
        if (e.PropertyName == nameof(FyersLoginViewModel.SecretId) && SecretIdBox.Password != _viewModel.SecretId)
            SecretIdBox.Password = _viewModel.SecretId;
        else if (e.PropertyName == nameof(FyersLoginViewModel.Pin) && PinBox.Password != _viewModel.Pin)
            PinBox.Password = _viewModel.Pin;
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
        if (_viewModel is null)
            return;
        if (sender == SecretIdBox)
            _viewModel.SecretId = SecretIdBox.Password;
        else if (sender == PinBox)
            _viewModel.Pin = PinBox.Password;
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Views;

/// <summary>PasswordBox.Password can't be bound, so it's synced with the view model here.</summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = e.NewValue as SettingsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            ApiKeyBox.Password = _viewModel.ApiKey;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.ApiKey) && _viewModel is not null && ApiKeyBox.Password != _viewModel.ApiKey)
            ApiKeyBox.Password = _viewModel.ApiKey;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.ApiKey = ApiKeyBox.Password;
    }
}

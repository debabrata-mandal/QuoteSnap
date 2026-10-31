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
            AdminApiKeyBox.Password = _viewModel.AdminApiKey;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;
        if (e.PropertyName == nameof(SettingsViewModel.ApiKey) && ApiKeyBox.Password != _viewModel.ApiKey)
            ApiKeyBox.Password = _viewModel.ApiKey;
        else if (e.PropertyName == nameof(SettingsViewModel.AdminApiKey) && AdminApiKeyBox.Password != _viewModel.AdminApiKey)
            AdminApiKeyBox.Password = _viewModel.AdminApiKey;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            return;
        if (sender == ApiKeyBox)
            _viewModel.ApiKey = ApiKeyBox.Password;
        else if (sender == AdminApiKeyBox)
            _viewModel.AdminApiKey = AdminApiKeyBox.Password;
    }
}

using System.Windows;
using System.Windows.Controls;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Views;

public partial class NewRequestView : UserControl
{
    public NewRequestView() => InitializeComponent();

    /// <summary>Shows the stock search results again when the box is clicked or focused after the dropdown closed.</summary>
    private void SymbolSearch_Reopen(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewRequestViewModel { SearchText.Length: > 0 } model)
            model.IsSearchOpen = true;
    }
}

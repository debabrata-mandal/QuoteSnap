using System.Windows.Controls;

namespace QuoteSnap.Desktop.Views;

public partial class StocksView : UserControl
{
    public StocksView() => InitializeComponent();

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => (List.Template.FindName("Scroller", List) as ScrollViewer)?.ScrollToTop();
}
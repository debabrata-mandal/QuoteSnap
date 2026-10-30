using System.Windows.Controls;
using System.Windows.Data;

namespace QuoteSnap.Desktop.Views;

public partial class HoldingsView : UserControl
{
    public HoldingsView() => InitializeComponent();

    /// <summary>A new fund, month or filter starts at the largest holding rather than where the last list was scrolled to.</summary>
    private void OnHoldingsReplaced(object? sender, DataTransferEventArgs e)
    {
        if (e.Property == ItemsControl.ItemsSourceProperty && HoldingsGrid.Items.Count > 0)
            HoldingsGrid.ScrollIntoView(HoldingsGrid.Items[0]);
    }
}

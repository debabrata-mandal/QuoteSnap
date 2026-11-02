using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using QuoteSnap.Desktop.ViewModels;

namespace QuoteSnap.Desktop.Views;

public partial class NavView : UserControl
{
    private Point _dragStart;
    private FundRow? _pressed;

    public NavView() => InitializeComponent();

    private NavViewModel? ViewModel => DataContext as NavViewModel;

    private void OnFundMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = ViewModel?.CanReorder == true && FindAncestor<ButtonBase>(e.OriginalSource) is null ? RowAt(e.OriginalSource) : null;
        _dragStart = e.GetPosition(this);
    }

    private async void OnFundMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressed is not { } row || e.LeftButton != MouseButtonState.Pressed || ViewModel is not { } vm)
            return;

        Vector moved = e.GetPosition(this) - _dragStart;
        if (Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        _pressed = null;
        List<int> before = [.. vm.MyFunds.Select(f => f.SchemeCode)];
        row.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(FundRow), row), DragDropEffects.Move);
        }
        finally
        {
            row.IsDragging = false;
        }

        await vm.SaveOrderAsync(before);
    }

    private void OnFundDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(FundRow)) is not FundRow dragged || ViewModel is not { } vm)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (RowAt(e.OriginalSource) is { } target && target != dragged)
            vm.MoveFund(dragged, target);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnFundDrop(object sender, DragEventArgs e) => e.Handled = true;

    private static FundRow? RowAt(object source) => FindAncestor<FrameworkElement>(source, e => e.DataContext is FundRow)?.DataContext as FundRow;

    private static T? FindAncestor<T>(object source, Func<T, bool>? match = null) where T : DependencyObject
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is T found && (match is null || match(found)))
                return found;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
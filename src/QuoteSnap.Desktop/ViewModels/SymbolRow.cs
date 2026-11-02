using CommunityToolkit.Mvvm.ComponentModel;
using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>A stock found by the New request page's symbol search.</summary>
public sealed partial class SymbolRow(FyersSymbolMatch match, bool isAdded) : ObservableObject
{
    public string Symbol { get; } = match.Symbol;

    public string Name { get; } = match.Name;

    public string Details => $"{match.Symbol} · ISIN {match.Isin}";

    /// <summary>Whether the symbol is already in the request's list.</summary>
    [ObservableProperty]
    public partial bool IsAdded { get; set; } = isAdded;
}

using CommunityToolkit.Mvvm.ComponentModel;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Settings;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>A fund on the user's list or in search results.</summary>
public sealed partial class FundRow : ObservableObject
{
    private FundRow(SavedFund fund, FundNav? nav, bool isAdded)
    {
        Fund = fund;
        Nav = nav;
        IsAdded = isAdded;
    }

    public SavedFund Fund { get; }

    /// <summary>Null until the NAVs load, or when the scheme is no longer in AMFI's file.</summary>
    public FundNav? Nav { get; }

    public int SchemeCode => Fund.SchemeCode;

    public string Name => Fund.SchemeName;

    public string? FundHouse => Fund.FundHouse;

    public string Subtitle => FundText.Subtitle(Fund.Option, Fund.Category);

    public string Initials => FundText.Initials(Fund.FundHouse, Fund.SchemeName);

    public string AvatarColor => FundText.AvatarColor(Fund.FundHouse ?? Fund.SchemeName);

    public string NavText => Nav is { } nav ? FundText.Nav(nav.Nav) : Display.Missing;

    public string NavDateText => Nav is { } nav ? Display.Date(nav.NavDate) : Display.Missing;

    public string Details => $"Scheme code {Fund.SchemeCode}" + (Nav?.Isin is { } isin ? $" · ISIN {isin}" : "");

    [ObservableProperty]
    public partial bool IsAdded { get; set; }

    /// <summary>The list was loaded but AMFI's file no longer has this scheme (merged or closed).</summary>
    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    public static FundRow FromNav(FundNav nav, bool isAdded) =>
        new(new SavedFund(nav.SchemeCode, nav.SchemeName, nav.FundHouse, nav.Option, nav.Category), nav, isAdded);

    public static FundRow FromSaved(SavedFund fund) => new(fund, null, isAdded: true);

    public static FundRow NotInFile(SavedFund fund) => new(fund, null, isAdded: true) { IsMissing = true };
}

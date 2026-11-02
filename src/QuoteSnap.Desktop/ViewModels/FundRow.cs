using CommunityToolkit.Mvvm.ComponentModel;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>A fund on the user's list or in search results.</summary>
public sealed partial class FundRow : ObservableObject
{
    private readonly string? _option;
    private readonly string? _category;

    private FundRow(int schemeCode, string name, string? fundHouse, string? option, string? category, FundNav? nav, bool isAdded)
    {
        SchemeCode = schemeCode;
        Name = name;
        FundHouse = fundHouse;
        _option = option;
        _category = category;
        Nav = nav;
        IsAdded = isAdded;
    }

    /// <summary>Null when NAVs aren't loaded, or when the scheme is no longer in AMFI's file.</summary>
    public FundNav? Nav { get; }

    public int SchemeCode { get; }

    public string Name { get; }

    public string? FundHouse { get; }

    public string Subtitle => FundText.Subtitle(_option, _category);

    public string Initials => FundText.Initials(FundHouse, Name);

    public string AvatarColor => FundText.AvatarColor(FundHouse ?? Name);

    public string NavText => Nav is { } nav ? FundText.Nav(nav.Nav) : Display.Missing;

    public string NavDateText => Nav is { } nav ? Display.Date(nav.NavDate) : Display.Missing;

    public string Details => $"Scheme code {SchemeCode}" + (Nav?.Isin is { } isin ? $" · ISIN {isin}" : "");

    [ObservableProperty]
    public partial bool IsAdded { get; set; }

    /// <summary>True while the user drags the row to a new place in the list.</summary>
    [ObservableProperty]
    public partial bool IsDragging { get; set; }

    /// <summary>Today's estimated move, from <see cref="ShowEstimate"/>; null until estimates are loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimateChangePercent), nameof(EstimateChangeText), nameof(EstimatedNavText), nameof(HasEstimatedNav))]
    public partial NavEstimate? Estimate { get; private set; }

    [ObservableProperty]
    public partial string? EstimateTip { get; private set; }

    public decimal? EstimateChangePercent => Estimate?.ChangePercent;

    public string EstimateChangeText => HoldingText.DayChange(Estimate?.ChangePercent);

    public bool HasEstimatedNav => Estimate?.EstimatedNav is not null;

    public string EstimatedNavText => Estimate?.EstimatedNav is { } nav ? FundText.Nav(nav) : Display.Missing;

    public void ShowEstimate(NavEstimate? estimate, DateTimeOffset? pricesAt)
    {
        Estimate = estimate;
        EstimateTip = estimate is null ? null : FundText.EstimateTip(estimate, pricesAt);
    }

    /// <summary>NAVs are loaded but AMFI's file no longer has this scheme (merged or closed).</summary>
    public bool IsMissing { get; private init; }

    public static FundRow FromNav(FundNav nav, bool isAdded) =>
        new(nav.SchemeCode, nav.SchemeName, nav.FundHouse, nav.Option, nav.Category, nav, isAdded);

    /// <param name="navsLoaded">Whether the service has NAVs at all; without them a missing NAV says nothing about the scheme.</param>
    public static FundRow FromMyFund(MyFund fund, bool navsLoaded) => fund.Latest is { } nav
        ? FromNav(nav, isAdded: true)
        : new(fund.SchemeCode, fund.SchemeName, fund.FundHouse, fund.Option, fund.Category, null, isAdded: true) { IsMissing = navsLoaded };
}

using CommunityToolkit.Mvvm.ComponentModel;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>A listed fund on the Holdings page, with what's stored for it and its progress in a running fetch.</summary>
public sealed partial class HoldingsFundRow(MyFund fund) : ObservableObject
{
    public int SchemeCode { get; } = fund.SchemeCode;

    public string Name { get; } = fund.Latest?.SchemeName ?? fund.SchemeName;

    public string Initials { get; } = FundText.Initials(fund.FundHouse, fund.SchemeName);

    public string AvatarColor { get; } = FundText.AvatarColor(fund.FundHouse ?? fund.SchemeName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText), nameof(HasHoldings))]
    public partial HoldingsSummary? Summary { get; set; } = fund.Holdings;

    public string SummaryText => HoldingText.Summary(Summary);

    public bool HasHoldings => Summary is not null;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Null when no fetch is being shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchStatusText), nameof(FetchLevel), nameof(HasFetchStatus))]
    public partial HoldingFetchFundStatus? FetchStatus { get; set; }

    [ObservableProperty]
    public partial string? FetchError { get; set; }

    public bool HasFetchStatus => FetchStatus is not null;

    public string FetchStatusText => FetchStatus is { } status ? HoldingText.FundStatus(status) : "";

    public StatusLevel FetchLevel => FetchStatus switch
    {
        HoldingFetchFundStatus.Ok => StatusLevel.Good,
        HoldingFetchFundStatus.Running => StatusLevel.Busy,
        HoldingFetchFundStatus.NotFound => StatusLevel.Warning,
        HoldingFetchFundStatus.Error => StatusLevel.Bad,
        _ => StatusLevel.Unknown,
    };
}

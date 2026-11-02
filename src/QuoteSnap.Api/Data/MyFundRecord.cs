namespace QuoteSnap.Api.Data;

/// <summary>
/// A fund on the user's list, by AMFI scheme code. The name and labels are copied from the NAV file when the fund is
/// added, so the fund stays recognisable if AMFI later drops the scheme (merged or wound up).
/// </summary>
public sealed class MyFundRecord
{
    public int SchemeCode { get; set; }

    public required string SchemeName { get; set; }

    public string? FundHouse { get; set; }

    public string? Option { get; set; }

    public string? Category { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Where the fund sits in the user's list, from 0; new funds go last.</summary>
    public int Position { get; set; }

    /// <summary>The ScanX scheme the holdings were last fetched from; null until the first fetch finds one.</summary>
    public int? ScanxPlanId { get; set; }

    public string? ScanxScheme { get; set; }

    public DateTimeOffset? HoldingsFetchedAt { get; set; }

    public List<FundHoldingRecord> Holdings { get; } = [];
}

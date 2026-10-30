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
}

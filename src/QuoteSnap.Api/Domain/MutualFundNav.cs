namespace QuoteSnap.Api.Domain;

/// <summary>One scheme's latest NAV from AMFI's daily NAVAll.txt.</summary>
public sealed class MutualFundNav
{
    /// <summary>AMFI scheme code, unique per scheme, plan and option.</summary>
    public int SchemeCode { get; set; }

    /// <summary>The "ISIN Div Payout/ ISIN Growth" column.</summary>
    public string? Isin { get; set; }

    /// <summary>The "ISIN Div Reinvestment" column.</summary>
    public string? IsinReinvestment { get; set; }

    public required string SchemeName { get; set; }

    public string? FundHouse { get; set; }

    /// <summary>The file's section heading, e.g. "Open Ended Schemes(Equity Scheme - Large Cap Fund)".</summary>
    public string? Category { get; set; }

    public string? Plan { get; set; }

    public string? Option { get; set; }

    public decimal Nav { get; set; }

    public DateOnly NavDate { get; set; }

    public DateTimeOffset LoadedAt { get; set; }
}

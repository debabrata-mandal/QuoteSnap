namespace QuoteSnap.Api.Data;

/// <summary>One instrument in a listed fund's portfolio at a month end, as scraped from ScanX.</summary>
public sealed class FundHoldingRecord
{
    public long Id { get; set; }

    public int SchemeCode { get; set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Month { get; set; }

    public required string AssetType { get; set; }

    public required string Name { get; set; }

    /// <summary>The Fyers symbol for the ISIN, e.g. NSE:RELIANCE-EQ (or BSE:… when not on NSE); null for unlisted lines.</summary>
    public string? Symbol { get; set; }

    public string? Isin { get; set; }

    public string? Sector { get; set; }

    /// <summary>Share of the fund's assets, in percent.</summary>
    public decimal Percent { get; set; }

    public decimal? Shares { get; set; }
}

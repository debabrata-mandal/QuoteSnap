namespace QuoteSnap.Api.Data;

/// <summary>A fund's NAV estimate as of the day's last finished holdings quote request; kept for <see cref="NavEstimateRepository.RetentionDays"/> days.</summary>
public sealed class NavEstimateRecord
{
    /// <summary>The server's local date the prices were fetched on.</summary>
    public DateOnly Day { get; set; }

    public int SchemeCode { get; set; }

    public DateTimeOffset? PricesAt { get; set; }

    public decimal? Nav { get; set; }

    public decimal? ChangePercent { get; set; }

    public decimal? EstimatedNav { get; set; }

    public int PricedHoldings { get; set; }

    /// <summary>The fund's position on the list when saved.</summary>
    public int Position { get; set; }

    public DateTimeOffset SavedAt { get; set; }
}

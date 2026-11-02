using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Live;

/// <summary>Streaming the top holdings' prices over the Fyers Data Socket.</summary>
public sealed class LiveQuotesOptions : IValidatableObject
{
    public const string SectionName = "LiveQuotes";

    public bool Enabled { get; set; } = true;

    /// <summary>How many of the top holdings to stream. Fyers' free plan allows 100 symbols per user across all sockets.</summary>
    [Range(1, 100)]
    public int SymbolCount { get; set; } = 95;

    /// <summary>Stream only on weekdays between <see cref="MarketOpen"/> and <see cref="MarketClose"/>; false streams around the clock.</summary>
    public bool MarketHoursOnly { get; set; }

    public TimeOnly MarketOpen { get; set; } = new(9, 15);

    public TimeOnly MarketClose { get; set; } = new(15, 30);

    /// <summary>The exchange's time zone, which market hours are in.</summary>
    [Required]
    public string TimeZone { get; set; } = "Asia/Kolkata";

    /// <summary>How often market hours, the session and the connection are checked.</summary>
    [Range(1, 3600)]
    public int CheckIntervalSeconds { get; set; } = 15;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MarketClose <= MarketOpen)
            yield return new ValidationResult("MarketClose must be after MarketOpen.", [nameof(MarketClose)]);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
            yield return new ValidationResult($"Time zone '{TimeZone}' isn't known.", [nameof(TimeZone)]);
    }
}

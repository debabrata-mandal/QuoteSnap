using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Fyers;

public sealed class FyersOptions
{
    public const string SectionName = "Fyers";

    /// <summary>When set, a login for any other Fyers account is rejected.</summary>
    public string? ExpectedFyersId { get; set; }

    [Required, Url]
    public string ApiBaseUrl { get; set; } = "https://api-t1.fyers.in/api/v3/";

    [Required, Url]
    public string DataBaseUrl { get; set; } = "https://api-t1.fyers.in/data/";

    /// <summary>Fyers accepts at most 50 symbols per quotes call.</summary>
    [Range(1, 50)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Pause between the quote batches of a request, so a long list is spread out instead of sent in a burst.
    /// MaxRequestsPerSecond and MaxRequestsPerMinute still cap every Fyers call on top of this.
    /// </summary>
    [Range(0, 10_000)]
    public int DelayBetweenBatchesMs { get; set; } = 250;

    [Range(1, 10)]
    public int MaxRequestsPerSecond { get; set; } = 5;

    [Range(1, 200)]
    public int MaxRequestsPerMinute { get; set; } = 150;
}

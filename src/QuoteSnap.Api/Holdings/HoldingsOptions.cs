using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Holdings;

/// <summary>Where and how fast fund holdings are scraped from ScanX (scanx.trade, by Dhan).</summary>
public sealed class HoldingsOptions
{
    public const string SectionName = "Holdings";

    /// <summary>The API behind scanx.trade/insight/mf-holdings; it serves the AMC list and each scheme's past holdings.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "https://ow-static-scanx.dhan.co/staticscanx/";

    /// <summary>Holdings rows per request. Large funds with many futures and bonds have over 600.</summary>
    [Range(25, 1000)]
    public int PageSize { get; set; } = 500;

    /// <summary>Pause between ScanX calls, to stay a polite visitor.</summary>
    [Range(0, 60_000)]
    public int DelayBetweenRequestsMs { get; set; } = 750;
}

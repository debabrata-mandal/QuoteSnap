using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Zerodha;

public sealed class ZerodhaOptions
{
    public const string SectionName = "Zerodha";

    /// <summary>When set, a login for any other Zerodha client ID is rejected.</summary>
    public string? ExpectedUserId { get; set; }

    [Required, Url]
    public string ApiBaseUrl { get; set; } = "https://api.kite.trade/";

    [Required, Url]
    public string LoginUrl { get; set; } = "https://kite.zerodha.com/connect/login";
}
